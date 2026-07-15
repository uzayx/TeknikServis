namespace TeknikServis.Application.DTOs;

public record CreateCustomerRequest(string FullName, string Email, string Phone, string? Address);
public record CustomerResponse(Guid Id, string FullName, string Email, string Phone, string? Address, DateTime CreatedAt);

public record CreateTechnicianRequest(string FullName, string Email, string Phone, string? Specialty);
public record TechnicianResponse(Guid Id, string FullName, string Email, string Phone, string? Specialty, bool IsActive, DateTime CreatedAt);
