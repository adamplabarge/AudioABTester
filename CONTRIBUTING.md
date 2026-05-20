# Contributing

Thanks for your interest in contributing to AudioABTester.

## Getting Started

1. Fork the repository.
2. Create a feature branch from `main`.
3. Make focused changes with clear commit messages.
4. Open a pull request with a summary of what changed and why.

## Development

Prerequisites:

- .NET 8 SDK
- Windows (for WPF build/run)

Build locally:

```bash
dotnet restore
dotnet build
```

Run locally:

```bash
dotnet run --project AudioABTester/AudioABTester.csproj
```

## Pull Request Guidelines

- Keep PRs small and focused.
- Add or update documentation when behavior changes.
- If you fix a bug, include a short reproduction note in the PR description.
- Ensure the project builds before opening the PR.

## Reporting Issues

Please include:

- Steps to reproduce
- Expected behavior
- Actual behavior
- OS and .NET SDK version
