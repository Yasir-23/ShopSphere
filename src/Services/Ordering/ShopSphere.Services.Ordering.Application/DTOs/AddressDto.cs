namespace ShopSphere.Services.Ordering.Application.DTOs;

public record AddressDto(
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country
);

