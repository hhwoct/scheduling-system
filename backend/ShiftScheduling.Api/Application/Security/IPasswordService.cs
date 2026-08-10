namespace ShiftScheduling.Api.Application.Security;

public interface IPasswordService
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}