using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests the registration workflow — validation rules, duplicate detection,
/// and the complete registration flow. Since RegisterViewModel depends on
/// the concrete GoogleSheetsService class (not testable via mock), these tests
/// exercise the same validation logic and state transitions directly.
/// </summary>
public class RegistrationWorkflowTests
{
    // ======================================================================
    // Validation rules (mirroring RegisterViewModel.RegisterAsync validation)
    // ======================================================================

    [Theory]
    [InlineData("", "Name is required")]
    [InlineData(null, "Name is required")]
    [InlineData("   ", "Name is required")]
    public void Validate_EmptyName_Fails(string? name, string expectedError)
    {
        var result = ValidateRegistration(name: name, email: "test@example.com",
            confirmEmail: "test@example.com", username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("", "Email is required")]
    [InlineData("   ", "Email is required")]
    public void Validate_EmptyEmail_Fails(string email, string expectedError)
    {
        var result = ValidateRegistration(name: "Alice", email: email,
            confirmEmail: email, username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("user@")]
    [InlineData("user@localhost")]
    [InlineData("@example.com")]
    [InlineData("user@b")]
    [InlineData("user name@example.com")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var result = ValidateRegistration(name: "Alice", email: email,
            confirmEmail: email, username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().NotBeNull();
        result.Should().Contain("email");
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("user.name@sub.domain.co")]
    [InlineData("test+tag@gmail.com")]
    public void Validate_ValidEmail_Passes(string email)
    {
        var result = ValidateRegistration(name: "Alice", email: email,
            confirmEmail: email, username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().BeNull("valid email should pass validation");
    }

    [Fact]
    public void Validate_EmailMismatch_Fails()
    {
        var result = ValidateRegistration(name: "Alice", email: "alice@example.com",
            confirmEmail: "bob@example.com", username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().Contain("do not match");
    }

    [Fact]
    public void Validate_EmailMatch_CaseInsensitive()
    {
        var result = ValidateRegistration(name: "Alice", email: "Alice@Example.COM",
            confirmEmail: "alice@example.com", username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().BeNull("email comparison should be case-insensitive");
    }

    [Theory]
    [InlineData("", "Login username is required")]
    [InlineData("   ", "Login username is required")]
    public void Validate_EmptyUsername_Fails(string username, string expectedError)
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: username,
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("short")]        // 5 chars, no digit
    [InlineData("12345")]        // 5 chars, all digits (too short)
    [InlineData("abcdef")]       // 6 chars, no digit
    [InlineData("abc")]          // too short and no digit
    public void Validate_WeakPassword_Fails(string password)
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: "user1",
            password: password, confirmPassword: password,
            gdpr: true, liability: true);

        result.Should().NotBeNull();
        result.Should().Contain("Password");
    }

    [Theory]
    [InlineData("secret1")]      // 7 chars, has digit
    [InlineData("123456a")]      // 7 chars, has letter and digit
    [InlineData("P@ssw0rd")]     // strong
    public void Validate_StrongPassword_Passes(string password)
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: "user1",
            password: password, confirmPassword: password,
            gdpr: true, liability: true);

        result.Should().BeNull("strong password should pass");
    }

    [Fact]
    public void Validate_PasswordMismatch_Fails()
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: "user1",
            password: "secret99", confirmPassword: "different1",
            gdpr: true, liability: true);

        result.Should().Contain("Passwords do not match");
    }

    [Fact]
    public void Validate_GdprNotAccepted_Fails()
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: false, liability: true);

        result.Should().Contain("GDPR");
    }

    [Fact]
    public void Validate_LiabilityNotAccepted_Fails()
    {
        var result = ValidateRegistration(name: "Alice", email: "a@b.com",
            confirmEmail: "a@b.com", username: "user1",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: false);

        result.Should().Contain("liability");
    }

    [Fact]
    public void Validate_AllValid_ReturnsNull()
    {
        var result = ValidateRegistration(name: "Alice Smith", email: "alice@example.com",
            confirmEmail: "alice@example.com", username: "alice42",
            password: "secret99", confirmPassword: "secret99",
            gdpr: true, liability: true);

        result.Should().BeNull("all fields valid should pass");
    }

    // ======================================================================
    // Duplicate detection (username & email)
    // ======================================================================

    [Fact]
    public void DuplicateCheck_UsernameTaken_Rejects()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "alice42", Email = "other@example.com" }
        };

        var taken = IsDuplicateUsername("alice42", existing);

        taken.Should().BeTrue();
    }

    [Fact]
    public void DuplicateCheck_UsernameCaseInsensitive()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "Alice42", Email = "other@example.com" }
        };

        IsDuplicateUsername("ALICE42", existing).Should().BeTrue();
        IsDuplicateUsername("alice42", existing).Should().BeTrue();
    }

    [Fact]
    public void DuplicateCheck_UsernameAvailable_Passes()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "bob@example.com" }
        };

        IsDuplicateUsername("alice42", existing).Should().BeFalse();
    }

    [Fact]
    public void DuplicateCheck_EmailTaken_Rejects()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "alice@example.com" }
        };

        IsDuplicateEmail("alice@example.com", existing).Should().BeTrue();
    }

    [Fact]
    public void DuplicateCheck_EmailCaseInsensitive()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "Alice@Example.COM" }
        };

        IsDuplicateEmail("alice@example.com", existing).Should().BeTrue();
    }

    [Fact]
    public void DuplicateCheck_EmailAvailable_Passes()
    {
        var existing = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "bob@example.com" }
        };

        IsDuplicateEmail("alice@example.com", existing).Should().BeFalse();
    }

    // ======================================================================
    // Fencer creation (the object that gets persisted)
    // ======================================================================

    [Fact]
    public void CreateFencer_ProducesCorrectModel()
    {
        var fencer = BuildRegistrationFencer(
            name: "Alice Smith",
            email: "alice@example.com",
            username: "alice42",
            password: "secret99",
            isStudent: true);

        fencer.Name.Should().Be("Alice Smith");
        fencer.Email.Should().Be("alice@example.com");
        fencer.Username.Should().Be("alice42");
        fencer.PasswordHash.Should().NotBeNullOrEmpty();
        fencer.PasswordHash.Should().NotBe("secret99", "password should be hashed");
        fencer.IsStudent.Should().BeTrue();
        fencer.IsInstructor.Should().BeFalse();
        fencer.Active.Should().BeTrue();
        fencer.GdprAccepted.Should().BeTrue();
        fencer.LiabilityAccepted.Should().BeTrue();
        fencer.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateFencer_PasswordHashedDeterministically()
    {
        var fencer1 = BuildRegistrationFencer(password: "secret99");
        var fencer2 = BuildRegistrationFencer(password: "secret99");

        fencer1.PasswordHash.Should().Be(fencer2.PasswordHash,
            "same password should produce the same hash");
    }

    [Fact]
    public void CreateFencer_DifferentPasswords_DifferentHashes()
    {
        var fencer1 = BuildRegistrationFencer(password: "secret99");
        var fencer2 = BuildRegistrationFencer(password: "other123");

        fencer1.PasswordHash.Should().NotBe(fencer2.PasswordHash);
    }

    // ======================================================================
    // EmailMismatch / PasswordMismatch computed properties
    // ======================================================================

    [Fact]
    public void EmailMismatch_BothEmpty_ReturnsFalse()
    {
        ComputeEmailMismatch("", "").Should().BeFalse();
    }

    [Fact]
    public void EmailMismatch_OneEmpty_ReturnsFalse()
    {
        ComputeEmailMismatch("test@example.com", "").Should().BeFalse();
        ComputeEmailMismatch("", "test@example.com").Should().BeFalse();
    }

    [Fact]
    public void EmailMismatch_BothFilledMatch_ReturnsFalse()
    {
        ComputeEmailMismatch("test@example.com", "test@example.com").Should().BeFalse();
        ComputeEmailMismatch("Test@Example.COM", "test@example.com").Should().BeFalse();
    }

    [Fact]
    public void EmailMismatch_BothFilledDiffer_ReturnsTrue()
    {
        ComputeEmailMismatch("alice@example.com", "bob@example.com").Should().BeTrue();
    }

    [Fact]
    public void PasswordMismatch_BothEmpty_ReturnsFalse()
    {
        ComputePasswordMismatch("", "").Should().BeFalse();
    }

    [Fact]
    public void PasswordMismatch_Match_ReturnsFalse()
    {
        ComputePasswordMismatch("secret99", "secret99").Should().BeFalse();
    }

    [Fact]
    public void PasswordMismatch_Differ_ReturnsTrue()
    {
        ComputePasswordMismatch("secret99", "other123").Should().BeTrue();
    }

    // ======================================================================
    // Full registration workflow end-to-end
    // ======================================================================

    [Fact]
    public void FullRegistration_NewUser_Succeeds()
    {
        var existingFencers = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "bob@example.com" }
        };

        // Step 1: Validate
        var validation = ValidateRegistration(
            name: "Alice Smith", email: "alice@example.com",
            confirmEmail: "alice@example.com", username: "alice42",
            password: "secure7x", confirmPassword: "secure7x",
            gdpr: true, liability: true);
        validation.Should().BeNull();

        // Step 2: Duplicate check
        IsDuplicateUsername("alice42", existingFencers).Should().BeFalse();
        IsDuplicateEmail("alice@example.com", existingFencers).Should().BeFalse();

        // Step 3: Create fencer
        var fencer = BuildRegistrationFencer(
            name: "Alice Smith", email: "alice@example.com",
            username: "alice42", password: "secure7x", isStudent: false);

        fencer.Name.Should().Be("Alice Smith");
        fencer.Active.Should().BeTrue();
        fencer.IsInstructor.Should().BeFalse();

        // Step 4: After registration, fencer can be found in the list
        existingFencers.Add(fencer);
        existingFencers.Should().HaveCount(2);
        existingFencers.Should().Contain(f => f.Username == "alice42");
    }

    [Fact]
    public void FullRegistration_DuplicateUsername_Blocked()
    {
        var existingFencers = new List<Fencer>
        {
            new() { Id = "f1", Username = "alice42", Email = "old@example.com" }
        };

        var validation = ValidateRegistration(
            name: "New Alice", email: "new@example.com",
            confirmEmail: "new@example.com", username: "alice42",
            password: "secure7x", confirmPassword: "secure7x",
            gdpr: true, liability: true);

        // Validation passes (no field errors)
        validation.Should().BeNull();

        // But duplicate check blocks
        IsDuplicateUsername("alice42", existingFencers).Should().BeTrue();
    }

    [Fact]
    public void FullRegistration_DuplicateEmail_Blocked()
    {
        var existingFencers = new List<Fencer>
        {
            new() { Id = "f1", Username = "bob", Email = "shared@example.com" }
        };

        var validation = ValidateRegistration(
            name: "Alice", email: "shared@example.com",
            confirmEmail: "shared@example.com", username: "alice42",
            password: "secure7x", confirmPassword: "secure7x",
            gdpr: true, liability: true);
        validation.Should().BeNull();

        IsDuplicateEmail("shared@example.com", existingFencers).Should().BeTrue();
    }

    [Fact]
    public void FullRegistration_StudentFlag_Persisted()
    {
        var fencer = BuildRegistrationFencer(isStudent: true);
        fencer.IsStudent.Should().BeTrue();

        var fencer2 = BuildRegistrationFencer(isStudent: false);
        fencer2.IsStudent.Should().BeFalse();
    }

    // ======================================================================
    // Helpers — these mirror the RegisterViewModel's private methods exactly
    // ======================================================================

    /// <summary>
    /// Replicates RegisterViewModel's validation chain. Returns null if valid,
    /// or the first error message.
    /// </summary>
    private static string? ValidateRegistration(
        string? name, string email, string confirmEmail,
        string username, string password, string confirmPassword,
        bool gdpr, bool liability)
    {
        var trimmedEmail = (email ?? "").Trim();
        var trimmedConfirmEmail = (confirmEmail ?? "").Trim();

        return
            string.IsNullOrWhiteSpace(name) ? "Name is required." :
            string.IsNullOrWhiteSpace(trimmedEmail) ? "Email is required." :
            !IsValidEmail(trimmedEmail) ? "Please enter a valid email address (e.g. you@example.com)." :
            string.IsNullOrWhiteSpace(trimmedConfirmEmail) ? "Please confirm your email address." :
            !string.Equals(trimmedEmail, trimmedConfirmEmail, StringComparison.OrdinalIgnoreCase)
                ? "Email addresses do not match." :
            string.IsNullOrWhiteSpace(username) ? "Login username is required." :
            !IsStrongPassword(password) ? "Password must be at least 6 characters and include at least one number." :
            password != confirmPassword ? "Passwords do not match." :
            !gdpr ? "You must accept the GDPR policy." :
            !liability ? "You must accept the liability statement." :
            null;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Contains(' ')) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            if (addr.Address != email) return false;
            var atIdx = email.LastIndexOf('@');
            if (atIdx < 1) return false;
            var host = email[(atIdx + 1)..];
            var dotIdx = host.LastIndexOf('.');
            if (dotIdx < 1) return false;
            if (host.Length - dotIdx - 1 < 2) return false;
            return true;
        }
        catch { return false; }
    }

    private static bool IsStrongPassword(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 6 &&
        password.Any(char.IsDigit);

    private static bool IsDuplicateUsername(string username, List<Fencer> existing) =>
        existing.Any(f =>
            !string.IsNullOrEmpty(f.Username) &&
            string.Equals(f.Username.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsDuplicateEmail(string email, List<Fencer> existing) =>
        existing.Any(f =>
            !string.IsNullOrEmpty(f.Email) &&
            string.Equals(f.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool ComputeEmailMismatch(string email, string confirmEmail) =>
        !string.IsNullOrWhiteSpace(email) &&
        !string.IsNullOrWhiteSpace(confirmEmail) &&
        !string.Equals(email.Trim(), confirmEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ComputePasswordMismatch(string password, string confirmPassword) =>
        !string.IsNullOrEmpty(password) &&
        !string.IsNullOrEmpty(confirmPassword) &&
        password != confirmPassword;

    private static Fencer BuildRegistrationFencer(
        string name = "Test User",
        string email = "test@example.com",
        string username = "testuser",
        string password = "secret99",
        bool isStudent = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Username = username.Trim(),
        PasswordHash = AuthService.Hash(password),
        Name = name.Trim(),
        Email = email.Trim(),
        Active = true,
        IsStudent = isStudent,
        GdprAccepted = true,
        LiabilityAccepted = true,
        IsInstructor = false
    };
}
