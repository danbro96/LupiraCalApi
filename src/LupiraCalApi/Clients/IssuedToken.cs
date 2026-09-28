namespace LupiraCalApi.Clients;

public sealed record IssuedToken(string AccessToken, TimeSpan ExpiresIn);
