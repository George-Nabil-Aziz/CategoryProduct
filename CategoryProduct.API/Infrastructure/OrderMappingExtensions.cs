using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;

namespace CategoryProduct.API.Infrastructure
{
    public static class OrderMappingExtensions
    {
        public static OrderResponseDto ToResponse(this Order order) =>
            new(order.Id, order.UserId, order.User?.UserName, order.Status.ToString(), order.Total,
                order.CreatedAtUtc, order.UpdatedAtUtc,
                order.Items.OrderBy(i => i.Id)
                    .Select(i => new OrderItemResponseDto(i.Id, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal))
                    .ToList(),
                order.PaymentStatus.ToString());

        public static CartResponseDto BuildCart(IEnumerable<CartItemResponseDto> items)
        {
            var list = items.ToList();
            return new CartResponseDto(list, list.Count, list.Sum(i => i.LineTotal));
        }
    }
}
