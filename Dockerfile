FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Explore.Shared/Explore.Shared.csproj src/Explore.Shared/
COPY src/Explore.Web/Explore.Web.csproj src/Explore.Web/
RUN dotnet restore src/Explore.Web/Explore.Web.csproj
COPY src/ src/
RUN dotnet publish src/Explore.Web/Explore.Web.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Api__BaseUrl=http://localhost:8080/
ENV ASPNETCORE_ENVIRONMENT=Production
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Explore.Web.dll"]
