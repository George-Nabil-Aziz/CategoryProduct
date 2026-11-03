namespace CategoryProduct.API.Dtos
{
    public record PagedResponseDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
}
