namespace TeknikServis.Application.DTOs;

public record CreateCustomerRequest(string FirstName, string LastName, string Email, string Phone, string? Address);
public record CustomerResponse(Guid Id, string FirstName, string LastName, string Email, string Phone, string? Address, DateTime CreatedAt)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record CreateTechnicianRequest(string FirstName, string LastName, string Email, string Phone, string? Specialty);
public record TechnicianResponse(Guid Id, string FirstName, string LastName, string Email, string Phone, string? Specialty, bool IsActive, DateTime CreatedAt)
{
    public string FullName => $"{FirstName} {LastName}";
}
