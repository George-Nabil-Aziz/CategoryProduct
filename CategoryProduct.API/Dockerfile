FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CategoryProduct.API/CategoryProduct.API.csproj CategoryProduct.API/
RUN dotnet restore CategoryProduct.API/CategoryProduct.API.csproj
COPY CategoryProduct.API/ CategoryProduct.API/
RUN dotnet publish CategoryProduct.API/CategoryProduct.API.csproj -c Release --no-restore -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
RUN mkdir -p /data/product-images && chown $APP_UID /data/product-images
COPY --from=build --chown=$APP_UID /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CategoryProduct.API.dll"]
