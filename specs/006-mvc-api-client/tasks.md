---

description: "Retroactive task list for MVC API client"
---

# Tasks: MVC Consumes the API

**Input**: Design documents from `/specs/006-mvc-api-client/`

**Tests**: None; verified manually against the running API.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Add `Api:BaseUrl` and remove connection strings in `CategoryProduct.MVC/appsettings.json`
- [X] T002 Register `AddHttpClient<CategoryProductApiClient>` in `CategoryProduct.MVC/Program.cs`
- [X] T003 [P] Remove EF Core packages from `CategoryProduct.MVC/CategoryProduct.MVC.csproj`
- [X] T004 [P] Delete `CategoryProduct.MVC/Data/ApplicationDbContext.cs`

## Phase 2: Client and models (US1, US2)

- [X] T005 [US1] Create `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` (list, get, create, update, delete, bearer hook)
- [X] T006 [P] [US1] Create `CategoryProduct.MVC/Models/ApiModels.cs`
- [X] T007 [P] [US2] Simplify `CategoryProduct.MVC/Models/Category.cs` and `CategoryProduct.MVC/Models/Product.cs`

## Phase 3: Controllers and views (US1)

- [X] T008 [US1] Rewrite `CategoryProduct.MVC/Controllers/CategoriesController.cs` on the client
- [X] T009 [US1] Rewrite `CategoryProduct.MVC/Controllers/ProductsController.cs` on the client
- [X] T010 [P] [US1] Adjust `CategoryProduct.MVC/Views/Categories/Create.cshtml`, `Delete.cshtml`, `Index.cshtml` (product-count column removed)
- [X] T011 [P] [US1] Adjust `CategoryProduct.MVC/Views/Products/Create.cshtml`, `Delete.cshtml`, `Details.cshtml`, `Index.cshtml`
