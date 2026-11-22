using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.API.Infrastructure
{
    public static class ProductMappingExtensions
    {
        public static ProductResponseDto ToResponse(this Product p, IUrlHelper url) =>
            new(p.Id, p.Name, p.Price, p.Quantity, p.CategoryId,
                p.ImageFileName == null ? null : url.ActionLink("GetImage", "Product", new { id = p.Id }));
    }
}
