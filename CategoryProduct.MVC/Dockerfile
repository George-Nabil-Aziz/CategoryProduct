FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CategoryProduct.MVC/CategoryProduct.MVC.csproj CategoryProduct.MVC/
RUN dotnet restore CategoryProduct.MVC/CategoryProduct.MVC.csproj
COPY CategoryProduct.MVC/ CategoryProduct.MVC/
RUN dotnet publish CategoryProduct.MVC/CategoryProduct.MVC.csproj -c Release --no-restore -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
COPY --from=build --chown=$APP_UID /app/publish .
RUN mkdir -p /data/dp-keys && chown $APP_UID /data/dp-keys
USER $APP_UID
ENTRYPOINT ["dotnet", "CategoryProduct.MVC.dll"]
