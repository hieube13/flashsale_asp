FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching
COPY FlashSale.slnx ./
COPY src/FlashSale.Domain/FlashSale.Domain.csproj           src/FlashSale.Domain/
COPY src/FlashSale.Contracts/FlashSale.Contracts.csproj     src/FlashSale.Contracts/
COPY src/FlashSale.Application/FlashSale.Application.csproj src/FlashSale.Application/
COPY src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj src/FlashSale.Infrastructure/
COPY src/FlashSale.Api/FlashSale.Api.csproj                 src/FlashSale.Api/

RUN dotnet restore FlashSale.slnx

# Copy everything else and publish
COPY . .
RUN dotnet publish src/FlashSale.Api/FlashSale.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

ENTRYPOINT ["dotnet", "FlashSale.Api.dll"]