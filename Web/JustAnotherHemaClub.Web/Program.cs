using System.Security.Claims;
using System.Threading.RateLimiting;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Web.Components;
using JustAnotherHemaClub.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering. The Google service-account
// JSON stays server-side (see ICredentialProvider): all Sheets I/O runs here,
// never in the browser.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The spreadsheet id and the service-account JSON path come from server config
// (appsettings.json / user-secrets / environment) instead of a bundled asset.
var spreadsheetId = builder.Configuration["GoogleSheets:SpreadsheetId"]
    ?? throw new InvalidOperationException("Missing config: GoogleSheets:SpreadsheetId");

// --- Core backends (same shape as MauiProgram) ---
builder.Services.AddSingleton<ICredentialProvider, WebCredentialProvider>();
builder.Services.AddSingleton(sp =>
    new GoogleSheetsService(spreadsheetId, sp.GetRequiredService<ICredentialProvider>()));
builder.Services.AddSingleton<CachedGoogleSheetsService>(sp =>
    new CachedGoogleSheetsService(sp.GetRequiredService<GoogleSheetsService>()));
builder.Services.AddSingleton<IGoogleSheetsService>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());
builder.Services.AddSingleton<ICacheControl>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());

builder.Services.AddSingleton<TestDataService>();

builder.Services.AddScoped<ICredentialStore, WebCredentialStore>();
builder.Services.AddScoped(sp =>
    new AuthService(sp, sp.GetRequiredService<ICredentialStore>()));

builder.Services.AddScoped<IDialogService, WebDialogService>();

builder.Services.AddScoped<UserSession>();

builder.Services.AddSingleton<TournamentAccessRegistry>();

// Backing store for per-account login lockout counters (web-only; keeps the
// Core AuthService free of an IMemoryCache dependency used by MAUI).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginLockout>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.Name = "jahc.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict bucket for auth endpoints, partitioned by client IP.
    options.AddPolicy("auth", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,                         // 5 attempts
            Window = TimeSpan.FromMinutes(1),        // per minute per IP
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });

    // Gentle global limiter as defense-in-depth.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var key = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

var app = builder.Build();

// The in-memory test backdoor is only allowed in Development.
AuthService.TestAccountEnabled = app.Environment.IsDevelopment();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// --- Security headers (defense in depth) ---
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    // Blazor Server needs inline styles and a websocket connection back to origin.
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();

// --- Auth endpoints (non-interactive; the Login form POSTs here) ---
app.MapPost("/auth/login", async (
    HttpContext http,
    AuthService auth,
    TestDataService testData,
    LoginLockout lockout,
    [Microsoft.AspNetCore.Mvc.FromForm] string username,
    [Microsoft.AspNetCore.Mvc.FromForm] string password,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
{
    var safeReturn = Uri.EscapeDataString(returnUrl ?? "/");

    // Per-account lockout: stop credential stuffing that rotates IPs.
    if (lockout.IsLockedOut(username))
        return Results.Redirect($"/login?error=locked&returnUrl={safeReturn}");

    if (!await auth.LoginAsync(username, password) || auth.CurrentFencer is null)
    {
        lockout.RegisterFailure(username);
        return Results.Redirect($"/login?error=1&returnUrl={safeReturn}");
    }

    lockout.Reset(username);

    if (auth.IsTestMode)
        ServiceSwap.Activate(testData);

    var f = auth.CurrentFencer;
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, f.Id ?? ""),
        new(ClaimTypes.Name, f.Username ?? ""),
        new("displayName", f.Name ?? ""),
        new("email", f.Email ?? ""),
        new("isStudent", f.IsStudent ? "1" : "0"),
        new("isTestMode", auth.IsTestMode ? "1" : "0"),
    };
    if (f.IsInstructor)
        claims.Add(new Claim(ClaimTypes.Role, "Instructor"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    return Results.Redirect(target);
}).RequireRateLimiting("auth");

app.MapPost("/auth/logout", async (HttpContext http, AuthService auth) =>
{
    auth.Logout();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// --- Register: self-service signup for new accounts (fencers only) ---
// Redirects to /login on success, or back to /register?error=... inline.
app.MapPost("/auth/register", async (
    IGoogleSheetsService sheets,
    ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name,
    [Microsoft.AspNetCore.Mvc.FromForm] string? email,
    [Microsoft.AspNetCore.Mvc.FromForm] string? confirmEmail,
    [Microsoft.AspNetCore.Mvc.FromForm] string? username,
    [Microsoft.AspNetCore.Mvc.FromForm] string? password,
    [Microsoft.AspNetCore.Mvc.FromForm] string? confirmPassword,
    [Microsoft.AspNetCore.Mvc.FromForm] string? isStudent,
    [Microsoft.AspNetCore.Mvc.FromForm] string? gdpr,
    [Microsoft.AspNetCore.Mvc.FromForm] string? liability) =>
{
    IResult Fail(string message) =>
        Results.Redirect(
            $"/register?error={Uri.EscapeDataString(message)}" +
            $"&name={Uri.EscapeDataString(name ?? "")}" +
            $"&email={Uri.EscapeDataString(email ?? "")}" +
            $"&confirmEmail={Uri.EscapeDataString(confirmEmail ?? "")}" +
            $"&username={Uri.EscapeDataString(username ?? "")}" +
            $"&isStudent={(isStudent == "true" ? "true" : "false")}" +
            $"&gdpr={(gdpr == "true" ? "true" : "false")}" +
            $"&liability={(liability == "true" ? "true" : "false")}");

    var student = isStudent == "true";
    var gdprOk = gdpr == "true";
    var liabilityOk = liability == "true";

    var validation = RegistrationValidator.Validate(
        name, email, confirmEmail, username,
        password, confirmPassword, gdprOk, liabilityOk);
    if (validation is not null)
        return Fail(validation);

    var trimmedEmail = (email ?? "").Trim();
    var desiredUser = (username ?? "").Trim();

    try
    {
        var existing = await sheets.GetFencersAsync();

        if (RegistrationValidator.IsDuplicateUsername(desiredUser, existing))
            return Fail("That username is already taken. Please choose another.");

        if (RegistrationValidator.IsDuplicateEmail(trimmedEmail, existing))
            return Fail("That email is already registered. Please use a different email or log in.");

        await sheets.AddFencerAsync(RegistrationValidator.BuildRegistrationFencer(
            name: name ?? "",
            email: trimmedEmail,
            username: desiredUser,
            password: password ?? "",
            isStudent: student,
            gdprAccepted: gdprOk,
            liabilityAccepted: liabilityOk));

        cache.InvalidateFencers();
        return Results.Redirect("/login?registered=1");
    }
    catch (Exception)
    {
        return Fail("Something went wrong while creating your account. Please try again later.");
    }
}).RequireRateLimiting("auth");

// --- Attend / un-attend the "Next lesson" shown on the Home card ---
// Mirrors HomeViewModel.ToggleAttendNextLessonAsync: toggles the signed-in
// fencer's id on the target training, materializing the recurring occurrence
// first when only a rule id was supplied.
app.MapPost("/home/attend", async (
    HttpContext http,
    IGoogleSheetsService sheets,
    ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? trainingId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? ruleId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? date,
    [Microsoft.AspNetCore.Mvc.FromForm] string? attending,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
{
    var fencerId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(fencerId))
        return Results.Redirect("/login");

    // Only allow local redirects back into the app.
    var target = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/')
        ? returnUrl
        : "/";

    var trainings = await sheets.GetTrainingsAsync();
    TrainingSession? session = null;

    if (!string.IsNullOrWhiteSpace(trainingId))
        session = trainings.FirstOrDefault(t => t.Id == trainingId);

    // Fall back to (or create) the recurring occurrence when we only have a rule.
    if (session is null && !string.IsNullOrWhiteSpace(ruleId) &&
        DateTime.TryParse(date, out var day))
    {
        var rules = await sheets.GetRecurringTrainingsAsync();
        var rule = rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is not null)
        {
            var id = $"rec_{rule.Id}_{day:yyyyMMdd}";
            var topicKey = (rule.Topic ?? "").Trim().ToLowerInvariant();

            session = trainings.FirstOrDefault(t => t.Id == id)
                ?? trainings.FirstOrDefault(t =>
                       t.Date.Date == day.Date &&
                       t.Date.TimeOfDay == rule.TimeOfDay &&
                       (t.Topic ?? "").Trim().ToLowerInvariant() == topicKey);

            if (session is null)
            {
                session = new TrainingSession
                {
                    Id = id,
                    Date = day.Date + rule.TimeOfDay,
                    EndDate = day.Date + rule.EndTimeOfDay,
                    Topic = rule.Topic ?? "",
                };
                await sheets.UpsertTrainingAsync(session);
            }
        }
    }

    if (session is null)
        return Results.Redirect(target);

    // "attending" reflects the state the button was showing; toggle away from it.
    var wasAttending = attending == "1";
    if (wasAttending)
        session.AttendeeFencerIds.Remove(fencerId);
    else if (!session.AttendeeFencerIds.Contains(fencerId))
        session.AttendeeFencerIds.Add(fencerId);

    await sheets.UpsertTrainingAsync(session);
    cache.InvalidateTrainings();

    return Results.Redirect(target);
}).RequireAuthorization().DisableAntiforgery();

// --- Instructor: save the full attendee list for a training ---
// The edit form submits a checkbox per fencer (name="attendee"); only the checked
// ids arrive, so we replace the attendee list with exactly that set.
app.MapPost("/trainings/attendees", async (
    HttpContext http,
    IGoogleSheetsService sheets,
    ICacheControl cache,
    AuthService auth) =>
{
    if (!http.User.IsInRole("Instructor"))
        return Results.Redirect("/trainings?tab=trainings");

    var form = await http.Request.ReadFormAsync();
    var trainingId = form["trainingId"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    var target = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/')
        ? returnUrl
        : "/trainings?tab=trainings";

    if (string.IsNullOrWhiteSpace(trainingId))
        return Results.Redirect(target);

    var trainings = await sheets.GetTrainingsAsync();
    var session = trainings.FirstOrDefault(t => t.Id == trainingId);
    if (session is null)
        return Results.Redirect(target);

    session.AttendeeFencerIds = form["attendee"]
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .Distinct()
        .ToList();

    await sheets.UpsertTrainingAsync(session);
    cache.InvalidateTrainings();

    return Results.Redirect(target);
}).RequireAuthorization().DisableAntiforgery();

// ================= WEEKLY (recurring) TRAINING RULES =================
// Instructor-only create / edit / delete. Regular fencers view them read-only.
app.MapPost("/weekly/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var rules = await sheets.GetRecurringTrainingsAsync();
    var rule = rules.FirstOrDefault(r => r.Id == form["ruleId"].ToString());
    if (rule is null) return Results.Redirect(back);

    rule.Topic = form["topic"].ToString();
    if (ParseTime(form["startTime"], out var st)) rule.TimeOfDay = st;
    if (ParseTime(form["endTime"], out var et)) rule.EndTimeOfDay = et;
    if (DateTime.TryParse(form["startDate"], out var sd)) rule.StartDate = sd;
    rule.EndDate = form["hasEndDate"] == "on" && DateTime.TryParse(form["endDate"], out var ed)
        ? ed : null;

    await sheets.UpsertRecurringTrainingAsync(rule);
    cache.InvalidateRecurringTrainings();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/weekly/add", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    if (!Enum.TryParse<DayOfWeek>(form["dayOfWeek"], out var dow)) dow = DayOfWeek.Tuesday;
    ParseTime(form["startTime"], out var st);
    ParseTime(form["endTime"], out var et);
    DateTime.TryParse(form["startDate"], out var sd);
    if (sd == default) sd = DateTime.Today;

    var rule = new RecurringTrainingRule
    {
        Id = Guid.NewGuid().ToString("N"),
        DayOfWeek = dow,
        TimeOfDay = st == default ? new TimeSpan(18, 0, 0) : st,
        EndTimeOfDay = et == default ? new TimeSpan(20, 0, 0) : et,
        Topic = form["topic"].ToString(),
        StartDate = sd,
        EndDate = form["hasEndDate"] == "on" && DateTime.TryParse(form["endDate"], out var ed) ? ed : null,
        CreatedByFencerId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
    };

    await sheets.UpsertRecurringTrainingAsync(rule);
    cache.InvalidateRecurringTrainings();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/weekly/delete", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var ruleId = form["ruleId"].ToString();
    if (!string.IsNullOrWhiteSpace(ruleId))
    {
        await sheets.DeleteRecurringTrainingAsync(ruleId);
        cache.InvalidateRecurringTrainings();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= 1-ON-1 (INDIVIDUAL) LESSONS =================
// Students request from instructor(s). Instructors add directly, request from
// other instructors, accept / reject requests, edit notes and delete.
app.MapPost("/lessons/create", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");

    var form = await http.Request.ReadFormAsync();
    DateTime.TryParse(form["date"], out var d);
    ParseTime(form["time"], out var tt);
    var when = (d == default ? DateTime.Today : d.Date) + tt;
    var topic = form["topic"].ToString();
    var mode = form["mode"].ToString(); // "direct" | "request"

    IndividualLesson lesson;
    if (http.User.IsInRole("Instructor") && mode == "direct")
    {
        var studentId = form["studentId"].ToString();
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Redirect(back);
        lesson = new IndividualLesson
        {
            Date = when,
            StudentId = studentId,
            InstructorId = string.IsNullOrWhiteSpace(form["instructorId"]) ? meId : form["instructorId"].ToString(),
            Topic = topic,
            Notes = form["notes"].ToString(),
            NextIdea = form["nextIdea"].ToString(),
            Status = IndividualLessonStatus.Accepted,
        };
    }
    else
    {
        // Request flow (students always; instructors in request mode).
        var targets = form["instructor"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        if (targets.Count == 0) return Results.Redirect(back);
        lesson = new IndividualLesson
        {
            Date = when,
            StudentId = meId,
            InstructorId = "",
            Topic = topic,
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = targets,
        };
    }

    await sheets.UpsertIndividualLessonAsync(lesson);
    cache.InvalidateIndividualLessons();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/accept", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.InstructorId = meId;
        l.Status = IndividualLessonStatus.Accepted;
        l.RequestedInstructorIds = new();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/reject", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.RequestedInstructorIds = l.RequestedInstructorIds.Where(id => id != meId).ToList();
        if (l.RequestedInstructorIds.Count == 0)
            l.Status = IndividualLessonStatus.Rejected;
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.Topic = form["topic"].ToString();
        l.Notes = form["notes"].ToString();
        l.NextIdea = form["nextIdea"].ToString();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/delete", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        // Reuse Rejected as a soft-delete, matching the MAUI app.
        l.Status = IndividualLessonStatus.Rejected;
        l.RequestedInstructorIds = new();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Profile: the signed-in fencer edits their own details ---
// Mirrors ProfileViewModel.SaveAsync: Name/Email/IsStudent/GDPR/Liability are
// editable; Username, IsInstructor and Active are not touched here.
app.MapPost("/profile/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name,
    [Microsoft.AspNetCore.Mvc.FromForm] string? email,
    [Microsoft.AspNetCore.Mvc.FromForm] string? isStudent,
    [Microsoft.AspNetCore.Mvc.FromForm] string? gdpr,
    [Microsoft.AspNetCore.Mvc.FromForm] string? liability) =>
{
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");

    try
    {
        var fencers = await sheets.GetFencersAsync();
        var me = fencers.FirstOrDefault(f => f.Id == meId);
        if (me is null) return Results.Redirect("/profile");

        me.Name = (name ?? "").Trim();
        me.Email = (email ?? "").Trim();
        me.IsStudent = isStudent == "on";
        me.GdprAccepted = gdpr == "on";
        me.LiabilityAccepted = liability == "on";

        await sheets.UpsertFencerAsync(me);
        cache.InvalidateFencers();
        return Results.Redirect("/profile?saved=1");
    }
    catch (Exception)
    {
        return Results.Redirect($"/profile?error={Uri.EscapeDataString("Could not save your profile. Please try again later.")}");
    }
}).RequireAuthorization().DisableAntiforgery();

// --- Finance: promote a member to instructor (instructor-only) ---
app.MapPost("/fencers/promote", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId) =>
{
    var back = $"/fencers?fencer={Uri.EscapeDataString(fencerId ?? "")}";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (string.IsNullOrWhiteSpace(fencerId)) return Results.Redirect("/fencers");

    var fencers = await sheets.GetFencersAsync();
    var target = fencers.FirstOrDefault(f => f.Id == fencerId);
    if (target is not null && !target.IsInstructor)
    {
        target.IsInstructor = true;
        await sheets.UpsertFencerAsync(target);
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= FINANCE =================
// Instructor-only money actions. Members never reach these (buttons are hidden),
// but the endpoints re-check the role server-side.
app.MapPost("/finance/markpaid", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (string.IsNullOrWhiteSpace(fencerId)) return Results.Redirect(back);

    if (!decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value) || value == 0m)
        return Results.Redirect(back);

    await sheets.MarkPaidAsync(new Payment
    {
        FencerId = fencerId,
        Year = year,
        Month = month,
        Amount = value,
        PaidOn = DateTime.Now,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/expense/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string? category,
    [Microsoft.AspNetCore.Mvc.FromForm] string? description,
    [Microsoft.AspNetCore.Mvc.FromForm] string? amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value);
    if (string.IsNullOrWhiteSpace(description) && value <= 0m) return Results.Redirect(back);

    var day = Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(year, month));
    await sheets.AddExpenseAsync(new Expense
    {
        Date = new DateTime(year, month, day),
        Category = category ?? "",
        Description = description ?? "",
        Amount = value,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/income/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string? category,
    [Microsoft.AspNetCore.Mvc.FromForm] string? description,
    [Microsoft.AspNetCore.Mvc.FromForm] string? amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value);
    if (string.IsNullOrWhiteSpace(description) && value <= 0m) return Results.Redirect(back);

    var day = Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(year, month));
    await sheets.AddIncomeAsync(new Income
    {
        Date = new DateTime(year, month, day),
        Category = category ?? "",
        Description = description ?? "",
        Amount = value,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    decimal.TryParse(form["fullPrice"], System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var full);
    if (full <= 0m) return Results.Redirect(back);
    decimal.TryParse(form["studentPrice"], System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var student);

    var (sessions, months, isCustom) = form["tier"].ToString() switch
    {
        "0" => (1, 1, false),
        "1" => (4, 1, false),
        "2" => (0, 1, false),
        _ => (0, 1, true),
    };
    var hasEnd = form["hasEndDate"] == "on";
    if (isCustom && !hasEnd) return Results.Redirect(back);

    DateTime.TryParse(form["startDate"], out var start);
    if (start == default) start = DateTime.Today;
    DateTime.TryParse(form["endDate"], out var end);

    await sheets.UpsertPriceRuleAsync(new PriceRule
    {
        SessionCount = sessions,
        MonthCount = months,
        IsCustomPeriod = isCustom,
        FullPrice = full,
        StudentPrice = student > 0 ? student : DuesCalculator.SuggestStudentPrice(full),
        StartDate = start,
        EndDate = hasEnd ? end : null,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/save", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var rules = await sheets.GetPriceRulesAsync();
    var rule = rules.FirstOrDefault(r => r.Id == form["ruleId"].ToString());
    if (rule is null) return Results.Redirect(back);

    if (decimal.TryParse(form["fullPrice"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var full)) rule.FullPrice = full;
    if (decimal.TryParse(form["studentPrice"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var student)) rule.StudentPrice = student;
    if (DateTime.TryParse(form["startDate"], out var start)) rule.StartDate = start;
    var hasEnd = form["hasEndDate"] == "on";
    rule.EndDate = hasEnd && DateTime.TryParse(form["endDate"], out var end) ? end : null;
    rule.IsCustomPeriod = form["isCustomPeriod"] == "on";
    if (rule.IsCustomPeriod && rule.EndDate is null) return Results.Redirect(back);

    await sheets.UpsertPriceRuleAsync(rule);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/delete", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string ruleId) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (!string.IsNullOrWhiteSpace(ruleId))
        await sheets.DeletePriceRuleAsync(ruleId);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= TOURNAMENTS =================
// The web app reads/writes the SAME sheets as the MAUI app, reusing the Core
// TournamentEngine + IGoogleSheetsService, so a PC and a phone can co-organise
// one tournament. Organiser access is granted per-user by entering the password
// (tracked server-side in TournamentAccessRegistry, the web parallel of the MAUI
// TournamentSession role).

static string? TournamentUserId(HttpContext http) =>
    http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

static bool IsTournamentOrganiser(HttpContext http, TournamentAccessRegistry access, string tournamentId) =>
    access.IsOrganiser(TournamentUserId(http), tournamentId);

// --- Create a tournament: the creator is auto-granted organiser access ---
app.MapPost("/tournaments/create", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name,
    [Microsoft.AspNetCore.Mvc.FromForm] string? password) =>
{
    var meId = TournamentUserId(http);
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        return Results.Redirect($"/tournaments?error={Uri.EscapeDataString("Name and password are required.")}");

    var t = new Tournament
    {
        Name = name.Trim(),
        PasswordPlain = password.Trim(),
        State = TournamentState.Setup,
        CreatedAt = DateTime.UtcNow,
    };
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    access.GrantOrganiser(meId, t.Id);
    return Results.Redirect($"/tournaments/{t.Id}");
}).RequireAuthorization().DisableAntiforgery();

// --- Delete a tournament (organiser only) ---
app.MapPost("/tournaments/delete", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect("/tournaments");
    await sheets.DeleteTournamentAsync(tournamentId);
    cache.InvalidateTournaments();
    return Results.Redirect("/tournaments");
}).RequireAuthorization().DisableAntiforgery();

// --- Unlock organiser access with the password ---
app.MapPost("/tournaments/access", async (
    HttpContext http, IGoogleSheetsService sheets, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? password) =>
{
    var meId = TournamentUserId(http);
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");

    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null) return Results.Redirect("/tournaments");

    if ((password ?? "").Trim() == (t.PasswordPlain ?? "").Trim() && !string.IsNullOrEmpty(t.PasswordPlain))
    {
        access.GrantOrganiser(meId, tournamentId);
        return Results.Redirect($"/tournaments/{tournamentId}");
    }
    return Results.Redirect($"/tournaments/{tournamentId}?error={Uri.EscapeDataString("Incorrect password.")}");
}).RequireAuthorization().DisableAntiforgery();

// --- Roster: add / remove fencer (Setup state, organiser) ---
app.MapPost("/tournaments/fencer/add", async (
    HttpContext http, IGoogleSheetsService sheets, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    var n = (name ?? "").Trim();
    if (n.Length == 0) return Results.Redirect(back);
    if (t.Fencers.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"'{n}' is already on the roster.")}");

    var fencer = new TournamentFencer { Name = n, OrderIndex = t.Fencers.Count };
    await sheets.UpsertTournamentFencerAsync(tournamentId, fencer);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/tournaments/fencer/remove", async (
    HttpContext http, IGoogleSheetsService sheets, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    await sheets.DeleteTournamentFencerAsync(tournamentId, fencerId);
    // Drop from any draft pool.
    foreach (var pool in t.Pools)
        if (pool.FencerIds.Remove(fencerId))
            await sheets.UpsertPoolAsync(tournamentId, pool);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Edit tournament details: name / organiser password (organiser) ---
// Mirrors the MAUI editor's auto-saved Name / Password fields.
app.MapPost("/tournaments/details", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name,
    [Microsoft.AspNetCore.Mvc.FromForm] string? password) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null) return Results.Redirect(back);

    var newName = (name ?? "").Trim();
    var newPassword = (password ?? "").Trim();
    if (newName.Length == 0)
        return Results.Redirect($"{back}?error={Uri.EscapeDataString("Name is required.")}");

    t.Name = newName;
    if (newPassword.Length > 0) t.PasswordPlain = newPassword;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Withdraw / reinstate a fencer (mirrors TournamentEditorVm.WithdrawFencerAsync) ---
// Setup       — just flips the withdrawn flag (fencer can't be picked into pools).
// Pools/Elim  — flips the flag AND walks over every unfinished match the fencer
//               is in (opponent wins 0-0), then propagates bracket advancements.
app.MapPost("/tournaments/fencer/withdraw", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null) return Results.Redirect(back);

    var fencer = t.Fencers.FirstOrDefault(f => f.Id == fencerId);
    if (fencer is null) return Results.Redirect(back);

    bool willBeWithdrawn = !fencer.IsWithdrawn;
    fencer.IsWithdrawn = willBeWithdrawn;

    try
    {
        TournamentEngine.WithdrawalCascade? cascade = null;
        if (willBeWithdrawn && t.State is not TournamentState.Setup and not TournamentState.Finished)
            cascade = TournamentEngine.ApplyWithdrawalCascade(t, fencerId);

        // Withdrawing during Setup also drops them from any draft pool.
        if (willBeWithdrawn && t.State == TournamentState.Setup)
            foreach (var pool in t.Pools)
                if (pool.FencerIds.Remove(fencerId))
                    await sheets.UpsertPoolAsync(tournamentId, pool);

        await sheets.UpsertTournamentFencerAsync(tournamentId, fencer);

        if (cascade is not null)
        {
            var changed = cascade.ChangedPoolMatches.Concat(cascade.ChangedBracketMatches).ToList();
            foreach (var m in changed)
                await sheets.UpsertMatchAsync(tournamentId, m);
        }
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Update failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Draft pools (Setup state, organiser) ---
app.MapPost("/tournaments/pool/auto", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    var active = t.Fencers.Where(f => !f.IsWithdrawn).ToList();
    if (active.Count < 4)
        return Results.Redirect($"{back}?error={Uri.EscapeDataString("Need at least 4 active fencers.")}");

    try
    {
        var draft = TournamentEngine.BuildDraftPools(active, new Random());
        // Clear existing visible pools, then persist the new draft set.
        foreach (var old in t.Pools)
        {
            old.FencerIds.Clear();
            old.Index = int.MaxValue;
            await sheets.UpsertPoolAsync(tournamentId, old);
        }
        for (int i = 0; i < draft.Count; i++)
        {
            draft[i].Index = i;
            await sheets.UpsertPoolAsync(tournamentId, draft[i]);
        }
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Auto-distribute failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/tournaments/pool/add", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    var visible = t.Pools.Where(p => p.Index != int.MaxValue).OrderBy(p => p.Index).ToList();
    var pool = new Pool { Index = visible.Count };
    await sheets.UpsertPoolAsync(tournamentId, pool);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Remove a single (empty) draft pool (Setup state, organiser). Its fencers
//     fall back to the unassigned list; remaining pools are re-indexed. ---
app.MapPost("/tournaments/pool/remove", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string poolId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    var target = t.Pools.FirstOrDefault(p => p.Id == poolId);
    if (target is null) return Results.Redirect(back);

    try
    {
        target.FencerIds.Clear();
        target.Index = int.MaxValue;
        await sheets.UpsertPoolAsync(tournamentId, target);

        var visible = t.Pools
            .Where(p => p.Index != int.MaxValue)
            .OrderBy(p => p.Index)
            .ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            if (visible[i].Index != i)
            {
                visible[i].Index = i;
                await sheets.UpsertPoolAsync(tournamentId, visible[i]);
            }
        }
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Remove pool failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/tournaments/pool/clear", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    try
    {
        // Only touch pools that still hold fencers or are still visible; setting
        // Index out of range drops them from the draft view.
        foreach (var pool in t.Pools.ToList())
        {
            if (pool.FencerIds.Count == 0 && pool.Index == int.MaxValue) continue;
            pool.FencerIds.Clear();
            pool.Index = int.MaxValue;
            await sheets.UpsertPoolAsync(tournamentId, pool);
        }
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Unassign failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/tournaments/pool/move", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? targetPoolId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    var affected = new List<Pool>();
    foreach (var pool in t.Pools)
        if (pool.FencerIds.Remove(fencerId)) affected.Add(pool);

    if (!string.IsNullOrEmpty(targetPoolId))
    {
        var target = t.Pools.FirstOrDefault(p => p.Id == targetPoolId);
        if (target is not null && !target.FencerIds.Contains(fencerId))
        {
            target.FencerIds.Add(fencerId);
            if (!affected.Contains(target)) affected.Add(target);
        }
    }

    try
    {
        foreach (var pool in affected)
            await sheets.UpsertPoolAsync(tournamentId, pool);
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Move failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Start the tournament: generate pool matches ---
app.MapPost("/tournaments/start", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Setup) return Results.Redirect(back);

    if (t.Fencers.Count(f => !f.IsWithdrawn) < 4)
        return Results.Redirect($"{back}?error={Uri.EscapeDataString("Need at least 4 active fencers.")}");

    const int minPoolSize = 4, maxPoolSize = 8;
    var draftPools = t.Pools
        .Where(p => p.Index != int.MaxValue && p.FencerIds.Count > 0)
        .OrderBy(p => p.Index)
        .ToList();

    List<Pool> pools;
    if (draftPools.Count > 0)
    {
        var bad = draftPools.Where(p => p.FencerIds.Count < minPoolSize || p.FencerIds.Count > maxPoolSize).ToList();
        if (bad.Count > 0)
            return Results.Redirect($"{back}?error={Uri.EscapeDataString("Every non-empty pool must have 4-8 fencers.")}");

        var assigned = new HashSet<string>(draftPools.SelectMany(p => p.FencerIds), StringComparer.Ordinal);
        var unassigned = t.Fencers.Where(f => !f.IsWithdrawn && !assigned.Contains(f.Id)).ToList();
        if (unassigned.Count > 0)
            return Results.Redirect($"{back}?error={Uri.EscapeDataString($"{unassigned.Count} fencer(s) are not assigned to a pool.")}");

        for (int i = 0; i < draftPools.Count; i++) draftPools[i].Index = i;
        TournamentEngine.GeneratePoolMatches(draftPools);
        pools = draftPools;
    }
    else
    {
        var active = t.Fencers.Where(f => !f.IsWithdrawn).ToList();
        pools = TournamentEngine.BuildPools(active, new Random());
    }

    try
    {
        foreach (var pool in pools)
            await sheets.UpsertPoolAsync(tournamentId, pool);
        await sheets.AppendMatchesAsync(tournamentId, pools.SelectMany(p => p.Matches).ToList());

        t.Pools = pools;
        t.State = TournamentState.PoolsInProgress;
        await sheets.UpsertTournamentHeaderAsync(t);
    }
    catch (Exception ex)
    {
        cache.InvalidateTournaments();
        return Results.Redirect($"{back}?error={Uri.EscapeDataString($"Start failed: {ex.Message}")}");
    }

    cache.InvalidateTournaments();
    return Results.Redirect($"{back}?tab=pools");
}).RequireAuthorization().DisableAntiforgery();

// --- Match scoring: points / cards / undo ---
app.MapPost("/tournaments/match/score", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string matchId,
    [Microsoft.AspNetCore.Mvc.FromForm] string action,
    [Microsoft.AspNetCore.Mvc.FromForm] string side,
    [Microsoft.AspNetCore.Mvc.FromForm] int delta,
    [Microsoft.AspNetCore.Mvc.FromForm] string? tab) =>
{
    var back = $"/tournaments/{tournamentId}/match/{matchId}?tab={tab}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State == TournamentState.Finished) return Results.Redirect(back);
    var match = FindTournamentMatch(t, matchId);
    if (match is null || match.Status == MatchStatus.Finished) return Results.Redirect(back);

    bool left = side == "left";
    switch (action)
    {
        case "point":
            if (left) match.LeftScore = Math.Max(0, match.LeftScore + delta);
            else match.RightScore = Math.Max(0, match.RightScore + delta);
            break;
        case "yellow":
            if (left) { if (match.LeftYellowCards == 0 && match.LeftRedCards == 0) match.LeftYellowCards++; }
            else { if (match.RightYellowCards == 0 && match.RightRedCards == 0) match.RightYellowCards++; }
            break;
        case "red":
            if (left) { match.LeftRedCards++; match.RightScore++; }
            else { match.RightRedCards++; match.LeftScore++; }
            break;
        case "undo":
            if (left)
            {
                if (match.LeftRedCards > 0) { match.LeftRedCards--; match.RightScore = Math.Max(0, match.RightScore - 1); }
                else if (match.LeftYellowCards > 0) match.LeftYellowCards--;
            }
            else
            {
                if (match.RightRedCards > 0) { match.RightRedCards--; match.LeftScore = Math.Max(0, match.LeftScore - 1); }
                else if (match.RightYellowCards > 0) match.RightYellowCards--;
            }
            break;
    }

    if (match.Status == MatchStatus.Pending) match.Status = MatchStatus.InProgress;
    match.UpdatedByUserId = TournamentUserId(http);
    await sheets.UpsertMatchAsync(tournamentId, match);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Match clock: +/- minute, reset ---
app.MapPost("/tournaments/match/clock", async (
    HttpContext http, IGoogleSheetsService sheets, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string matchId,
    [Microsoft.AspNetCore.Mvc.FromForm] string action,
    [Microsoft.AspNetCore.Mvc.FromForm] string? tab) =>
{
    var back = $"/tournaments/{tournamentId}/match/{matchId}?tab={tab}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State == TournamentState.Finished) return Results.Redirect(back);
    var match = FindTournamentMatch(t, matchId);
    if (match is null || match.Status == MatchStatus.Finished) return Results.Redirect(back);

    switch (action)
    {
        case "addminute": match.RemainingTimeSeconds += 60; break;
        case "subminute": if (match.RemainingTimeSeconds - 60 >= 120) match.RemainingTimeSeconds -= 60; break;
        case "restart": match.RemainingTimeSeconds = TournamentEngine.DefaultMatchSeconds; break;
    }
    await sheets.UpsertMatchAsync(tournamentId, match);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Finish a match (mirrors MatchViewModel.FinishMatchAsync) ---
app.MapPost("/tournaments/match/finish", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string matchId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? tab) =>
{
    var back = $"/tournaments/{tournamentId}/match/{matchId}?tab={tab}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State == TournamentState.Finished) return Results.Redirect(back);
    var match = FindTournamentMatch(t, matchId);
    if (match is null || match.Status == MatchStatus.Finished) return Results.Redirect(back);

    bool isElim = match.BracketRound.HasValue;
    if (isElim && match.LeftScore == match.RightScore)
        return Results.Redirect($"{back}&error={Uri.EscapeDataString("Elimination matches cannot end in a tie.")}");

    match.Status = MatchStatus.Finished;
    match.WinnerFencerId = match.LeftScore == match.RightScore
        ? null
        : (match.LeftScore > match.RightScore ? match.LeftFencerId : match.RightFencerId);
    match.FinishedAtUtc = DateTime.UtcNow;
    match.LockedByUserId = null;
    match.LockedAtUtc = null;
    match.UpdatedByUserId = TournamentUserId(http);
    await sheets.UpsertMatchAsync(tournamentId, match);

    if (isElim && t.Bracket is not null)
    {
        TournamentEngine.PatchInBracket(t.Bracket, match);
        var downstream = TournamentEngine.PropagateAndCollectChanges(t.Bracket);
        foreach (var m in downstream)
        {
            if (m.Id == match.Id) continue;
            try { await sheets.UpsertMatchAsync(tournamentId, m); } catch { }
        }

        if (TournamentEngine.IsBracketComplete(t.Bracket) && t.State != TournamentState.Finished)
        {
            var order = TournamentEngine.ComputeFinalStandings(t);
            await sheets.SaveFinalStandingsAsync(tournamentId, order);
            t.FinalStandingFencerIds = order;
            t.State = TournamentState.Finished;
            await sheets.UpsertTournamentHeaderAsync(t);
        }
    }

    cache.InvalidateTournaments();
    return Results.Redirect($"/tournaments/{tournamentId}?tab={tab}");
}).RequireAuthorization().DisableAntiforgery();

// --- Reopen a finished match ---
app.MapPost("/tournaments/match/reopen", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] string matchId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? tab) =>
{
    var back = $"/tournaments/{tournamentId}/match/{matchId}?tab={tab}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State == TournamentState.Finished) return Results.Redirect(back);
    var match = FindTournamentMatch(t, matchId);
    if (match is null || match.Status != MatchStatus.Finished) return Results.Redirect(back);

    match.Status = MatchStatus.InProgress;
    match.WinnerFencerId = null;
    match.FinishedAtUtc = null;
    match.UpdatedByUserId = TournamentUserId(http);
    await sheets.UpsertMatchAsync(tournamentId, match);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Elimination: generate bracket by size ---
app.MapPost("/tournaments/elim/generate", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId,
    [Microsoft.AspNetCore.Mvc.FromForm] int size) =>
{
    var back = $"/tournaments/{tournamentId}?tab=elim";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.Bracket is not null) return Results.Redirect(back);
    if (t.Pools.SelectMany(p => p.Matches).Any(m => m.Status != MatchStatus.Finished))
        return Results.Redirect($"{back}?error={Uri.EscapeDataString("Finish every pool match first.")}");

    var bracket = TournamentEngine.BuildBracketFromPoolStandingsBySize(t, size);
    t.Bracket = bracket;

    var initial = new List<Match>(bracket.Rounds.SelectMany(r => r.Matches));
    if (bracket.BronzeMatch is not null) initial.Add(bracket.BronzeMatch);
    await sheets.AppendMatchesAsync(tournamentId, initial);

    var changed = TournamentEngine.PropagateAndCollectChanges(bracket);
    foreach (var m in changed)
        await sheets.UpsertMatchAsync(tournamentId, m);

    t.State = TournamentState.EliminationInProgress;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Elimination: reset bracket back to PoolsClosed ---
app.MapPost("/tournaments/elim/reset", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}?tab=elim";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.Bracket is null) return Results.Redirect(back);

    var bracketMatches = t.Bracket.Rounds.SelectMany(r => r.Matches).ToList();
    if (t.Bracket.BronzeMatch is not null) bracketMatches.Add(t.Bracket.BronzeMatch);
    foreach (var m in bracketMatches)
        await sheets.DeleteMatchAsync(tournamentId, m.Id);

    await sheets.SaveFinalStandingsAsync(tournamentId, Array.Empty<string>());
    t.Bracket = null;
    t.FinalStandingFencerIds = new List<string>();
    t.State = TournamentState.PoolsClosed;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- End tournament (publish final standings) ---
app.MapPost("/tournaments/end", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}?tab=final";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.Bracket is null || !TournamentEngine.IsBracketComplete(t.Bracket))
        return Results.Redirect($"/tournaments/{tournamentId}?tab=elim");

    var order = TournamentEngine.ComputeFinalStandings(t);
    await sheets.SaveFinalStandingsAsync(tournamentId, order);
    t.FinalStandingFencerIds = order;
    t.State = TournamentState.Finished;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Reopen a finished tournament ---
app.MapPost("/tournaments/reopen", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State != TournamentState.Finished) return Results.Redirect(back);

    t.State = t.Bracket is not null ? TournamentState.EliminationInProgress : TournamentState.PoolsClosed;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Restart tournament back to Setup ---
app.MapPost("/tournaments/restart", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, TournamentAccessRegistry access,
    [Microsoft.AspNetCore.Mvc.FromForm] string tournamentId) =>
{
    var back = $"/tournaments/{tournamentId}";
    if (!IsTournamentOrganiser(http, access, tournamentId)) return Results.Redirect(back);
    var t = await sheets.GetTournamentAsync(tournamentId);
    if (t is null || t.State == TournamentState.Setup) return Results.Redirect(back);

    var allMatches = t.Pools.SelectMany(p => p.Matches).ToList();
    if (t.Bracket is not null)
    {
        allMatches.AddRange(t.Bracket.Rounds.SelectMany(r => r.Matches));
        if (t.Bracket.BronzeMatch is not null) allMatches.Add(t.Bracket.BronzeMatch);
    }
    foreach (var m in allMatches)
        await sheets.DeleteMatchAsync(tournamentId, m.Id);

    await sheets.SaveFinalStandingsAsync(tournamentId, Array.Empty<string>());

    foreach (var pool in t.Pools)
    {
        pool.FencerIds.Clear();
        pool.Matches.Clear();
        pool.IsClosed = false;
        pool.Index = int.MaxValue;
        await sheets.UpsertPoolAsync(tournamentId, pool);
    }

    foreach (var f in t.Fencers.Where(f => f.IsWithdrawn))
    {
        f.IsWithdrawn = false;
        await sheets.UpsertTournamentFencerAsync(tournamentId, f);
    }

    t.Pools = new List<Pool>();
    t.Bracket = null;
    t.FinalStandingFencerIds = new List<string>();
    t.State = TournamentState.Setup;
    await sheets.UpsertTournamentHeaderAsync(t);
    cache.InvalidateTournaments();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= PASSWORD RESET (instructor-mediated) =================
// The fencer proves identity (username + email) AND supplies the new password up
// front. We park the new password's PBKDF2 hash in Fencer.PendingPasswordHash
// (live PasswordHash untouched) until an instructor approves. Stored on the
// shared sheet, so a request filed in either the web or MAUI app is visible to
// instructors in both, and approval takes effect everywhere.
app.MapPost("/auth/forgot-password", async (
    IGoogleSheetsService sheets,
    ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? username,
    [Microsoft.AspNetCore.Mvc.FromForm] string? email,
    [Microsoft.AspNetCore.Mvc.FromForm] string? newPassword,
    [Microsoft.AspNetCore.Mvc.FromForm] string? confirmPassword) =>
{
    if (!RegistrationValidator.IsStrongPassword(newPassword))
        return Results.Redirect($"/forgot-password?error={Uri.EscapeDataString("Password must be at least 8 characters and include a letter and a number.")}");
    if (newPassword != confirmPassword)
        return Results.Redirect($"/forgot-password?error={Uri.EscapeDataString("Passwords do not match.")}");

    var user = (username ?? "").Trim();
    var mail = (email ?? "").Trim();

    var fencers = await sheets.GetFencersAsync();
    var match = fencers.FirstOrDefault(f =>
        !string.IsNullOrWhiteSpace(f.Username) &&
        string.Equals(f.Username.Trim(), user, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(f.Email) &&
        string.Equals(f.Email.Trim(), mail, StringComparison.OrdinalIgnoreCase));

    if (match is not null)
    {
        // Park the requested password; live PasswordHash stays until approval.
        match.PendingPasswordHash = PasswordHasher.Hash(AuthService.Hash(newPassword!));
        match.PasswordResetRequestedAtUtc = DateTime.UtcNow;
        await sheets.UpsertFencerAsync(match);
        cache.InvalidateFencers();
    }

    // Uniform response whether or not the identity matched (no enumeration).
    return Results.Redirect("/forgot-password?requested=1");
})
.RequireRateLimiting("auth");

// --- Instructor: approve / reject a pending password reset. Approve promotes the
//     parked hash to the live password; reject just clears the pending fields. ---
app.MapPost("/instructor/reset/review", async (
    HttpContext http,
    IGoogleSheetsService sheets,
    ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId,
    [Microsoft.AspNetCore.Mvc.FromForm] string decision) =>
{
    if (!http.User.IsInRole("Instructor")) return Results.Redirect("/");
    if (string.IsNullOrWhiteSpace(fencerId)) return Results.Redirect("/");

    var fencers = await sheets.GetFencersAsync();
    var target = fencers.FirstOrDefault(f => f.Id == fencerId);
    if (target is null || !target.HasPendingPasswordReset) return Results.Redirect("/");

    if (decision == "approve")
        target.PasswordHash = target.PendingPasswordHash;   // promote the parked hash

    // Both approve and reject clear the pending request.
    target.PendingPasswordHash = null;
    target.PasswordResetRequestedAtUtc = null;

    await sheets.UpsertFencerAsync(target);
    cache.InvalidateFencers();

    return Results.Redirect("/");
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool ParseTime(string? s, out TimeSpan t) => TimeSpan.TryParse(s, out t);

static Match? FindTournamentMatch(Tournament t, string id)
{
    foreach (var pool in t.Pools)
    {
        var m = pool.Matches.FirstOrDefault(m => m.Id == id);
        if (m is not null) return m;
    }
    if (t.Bracket is not null)
    {
        foreach (var round in t.Bracket.Rounds)
        {
            var m = round.Matches.FirstOrDefault(m => m.Id == id);
            if (m is not null) return m;
        }
        if (t.Bracket.BronzeMatch?.Id == id) return t.Bracket.BronzeMatch;
    }
    return null;
}