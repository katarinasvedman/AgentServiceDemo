FROM mcr.microsoft.com/dotnet/aspnet:8.0-nanoserver-1809 AS base
WORKDIR /app
EXPOSE 5244

ENV ASPNETCORE_URLS=http://+:5244

FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-1809 AS build
ARG configuration=Release
WORKDIR /src
COPY ["FoundryAgent.AppHost/FoundryAgent.AppHost.csproj", "FoundryAgent.AppHost/"]
RUN dotnet restore "FoundryAgent.AppHost\FoundryAgent.AppHost.csproj"
COPY . .
WORKDIR "/src/FoundryAgent.AppHost"
RUN dotnet build "FoundryAgent.AppHost.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "FoundryAgent.AppHost.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoundryAgent.AppHost.dll"]
