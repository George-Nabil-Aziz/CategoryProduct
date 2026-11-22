# Quickstart: Product Images

Prerequisites: API running with a JWT key configured and an Admin user; MVC running (or `docker compose up --build` once 016 is in place).

1. Log in: `POST api/account/login` and keep the token in `$TOKEN`.
2. Upload (on Windows PowerShell use `curl.exe`):
   `curl -k -X POST -H "Authorization: Bearer $TOKEN" -F "file=@photo.png;type=image/png" https://localhost:7183/api/product/1/image` returns 200 with `imageUrl`.
3. Fetch: `curl -k -o out.png https://localhost:7183/api/product/1/image` returns identical bytes.
4. Rejections: a `.txt` renamed to `.png` returns 400 (`file`); a file just over 2 MB returns 400; a 10 MB file returns 413; no token returns 401; a plain-user token returns 403.
5. Replace: upload another image; confirm only one `1-*` file is in `CategoryProduct.API/App_Data/product-images/`.
6. Remove: `curl -k -X DELETE -H "Authorization: Bearer $TOKEN" https://localhost:7183/api/product/1/image` returns 204; the file is gone.
7. Upload again, then delete the product: no file is left in the directory.
8. MVC: sign in as Admin, open Products > Details; upload a png; the image shows; View Source shows `src="/Products/Image/1"` and no API host. Sign out: the image is still visible, the form is hidden.
9. Docker (if 016 is present): `docker compose up --build`, upload an image, `docker compose down` (no `-v`), `up`: the image is still served.
10. Tests: `dotnet test CategoryProduct.slnx` (all green, including the new `ProductImageTests`).
