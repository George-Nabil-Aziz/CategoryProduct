namespace CategoryProduct.API.Services
{
    public interface IProductImageStorage
    {
        /// <summary>Writes the content to a new server-generated file and returns its name.</summary>
        Task<string> SaveAsync(int productId, Stream content, string ext, CancellationToken cancellationToken = default);

        /// <summary>Best-effort delete; never throws.</summary>
        void Delete(string? fileName);

        /// <summary>Returns the full path of an existing stored file, or null if the name is unsafe or the file is missing.</summary>
        string? TryGetPath(string fileName);
    }
}
