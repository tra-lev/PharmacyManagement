FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["PharmacyManagement/PharmacyManagement.csproj", "PharmacyManagement/"]
RUN dotnet restore "PharmacyManagement/PharmacyManagement.csproj"
COPY . .
WORKDIR "/src/PharmacyManagement"
RUN dotnet build "PharmacyManagement.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmacyManagement.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmacyManagement.dll"]