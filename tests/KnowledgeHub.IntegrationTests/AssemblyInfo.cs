// Все тесты делят одну БД и сбрасывают её перед каждым тестом, поэтому классы нельзя запускать параллельно:
// одна коллекция на сборку выполняется последовательно.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
