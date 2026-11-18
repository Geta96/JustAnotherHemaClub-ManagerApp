namespace JustAnotherHemaClub.Services;

public interface INotificationService
{
    void Show(int id, string title, string message);
}