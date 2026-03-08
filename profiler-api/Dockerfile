FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ProfilerApi/ProfilerApi.csproj ProfilerApi/
RUN dotnet restore ProfilerApi/ProfilerApi.csproj
COPY ProfilerApi/ ProfilerApi/
RUN dotnet publish ProfilerApi/ProfilerApi.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "ProfilerApi.dll"]
