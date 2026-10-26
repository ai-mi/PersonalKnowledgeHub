using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

/// <summary>Хранит перечисление строкой в нижнем регистре (<c>Weekly</c> → <c>weekly</c>), без PostgreSQL enum.</summary>
public sealed class LowercaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => value.ToString().ToLowerInvariant(),
    text => Enum.Parse<TEnum>(text, true))
    where TEnum : struct, Enum;
