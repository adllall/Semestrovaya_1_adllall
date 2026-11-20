FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source
COPY Semestrovaya/Semestrovaya.csproj Semestrovaya/
RUN dotnet restore Semestrovaya/Semestrovaya.csproj
COPY Semestrovaya/ Semestrovaya/
RUN dotnet publish Semestrovaya/Semestrovaya.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /out/ ./
ENV HTTP_PREFIX=http://+:8080/
EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "Semestrovaya.dll"]
