FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MBOS.Hub.TransactionMatching.sln ./
COPY TransactionMatching.Web/TransactionMatching.Web.csproj TransactionMatching.Web/
COPY TransactionMatching.Application/TransactionMatching.Application.csproj TransactionMatching.Application/
COPY TransactionMatching.Core/TransactionMatching.Core.csproj TransactionMatching.Core/
COPY TransactionMatching.Infrastructure/TransactionMatching.Infrastructure.csproj TransactionMatching.Infrastructure/
RUN dotnet restore TransactionMatching.Web/TransactionMatching.Web.csproj

COPY TransactionMatching.Web/ TransactionMatching.Web/
COPY TransactionMatching.Application/ TransactionMatching.Application/
COPY TransactionMatching.Core/ TransactionMatching.Core/
COPY TransactionMatching.Infrastructure/ TransactionMatching.Infrastructure/

RUN dotnet publish TransactionMatching.Web/TransactionMatching.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TransactionMatching.Web.dll"]
