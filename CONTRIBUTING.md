# Contributing to QuotaWidget

## Building the Project

Prerequisites: .NET 8 SDK.

```powershell
dotnet build QuotaWidget.sln -c Release
dotnet test QuotaWidget.sln
```

## Guidelines

- All business logic, parsers, formatting, and resolvers must remain pure and covered by unit tests.
- Do not invoke external processes or live network calls in unit tests.
- Ensure all tests pass with zero warnings and zero errors before submitting pull requests.
