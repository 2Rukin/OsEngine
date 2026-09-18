# Автономные тесты статистики

`dotnet run --project project/Tests/Statistics/OsEngine.Statistics.Tests.csproj`

Запускать из корня репозитория с .NET SDK 10. Это консольный regression runner,
не MCP-стенд: не запускает OsEngine, серверы, ордера и сетевые соединения.
Собирает те же три исходных файла ядра, что и основной продукт, без подмен логики.
Не требует NuGet test framework. Ошибка любого теста возвращает ненулевой exit code.
Файловые тесты создают и удаляют только собственный уникальный временный каталог.

Для полной сборки продукта на Windows: `dotnet build project/OsEngine.sln`.
Проверка WPF-окна вручную описана в `project/Docs/statistics.md`.
