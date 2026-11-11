using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AiInterview.Tests;

public class PersistenceModelTests
{
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelMetadataOnly;Trusted_Connection=True;")
        .Options);

    [Fact]
    public void EveryDomainEntityHasAKeyAndNoUnexpectedShadowForeignKeys()
    {
        using var db = Context();
        var domainTypes = typeof(InterviewSession).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && x.Namespace == typeof(InterviewSession).Namespace).ToArray();
        Assert.Equal(21, domainTypes.Length);
        foreach (var type in domainTypes)
        {
            var entity = db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.NotNull(entity.FindPrimaryKey());
            Assert.All(entity.GetForeignKeys(), foreignKey =>
            {
                Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
                Assert.All(foreignKey.Properties, property => Assert.False(property.IsShadowProperty(), $"Unexpected shadow FK {type.Name}.{property.Name}"));
            });
        }
    }

    [Theory]
    [InlineData(typeof(StudentProfile), "UserId")]
    [InlineData(typeof(CvProfile), "StudentId")]
    [InlineData(typeof(JobDescription), "StudentId")]
    [InlineData(typeof(InterviewSession), "StudentId")]
    [InlineData(typeof(AiRequestLog), "UserId")]
    [InlineData(typeof(Classroom), "LecturerId")]
    [InlineData(typeof(ClassMember), "StudentId")]
    [InlineData(typeof(AssignmentSubmission), "StudentId")]
    [InlineData(typeof(LecturerComment), "LecturerId")]
    public void OwnershipReferencesARealIdentityUser(Type entityType, string userProperty)
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(entityType)!;
        var relationship = Assert.Single(entity.GetForeignKeys(), x => x.Properties.Any(p => p.Name == userProperty));
        Assert.Equal(typeof(ApplicationUser), relationship.PrincipalEntityType.ClrType);
        Assert.True(relationship.IsRequired);
    }

    [Theory]
    [InlineData(typeof(StudentProfile), "UserId")]
    [InlineData(typeof(TurnEvaluation), "TurnId")]
    [InlineData(typeof(DeliveryMetric), "TurnId")]
    [InlineData(typeof(Evaluation), "SessionId")]
    public void OneToOneRecordsUseTheirParentKey(Type entityType, string parentKey)
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(entityType)!;
        Assert.Equal(parentKey, Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Contains(entity.GetForeignKeys(), x => x.IsUnique && x.Properties.Count == 1 && x.Properties[0].Name == parentKey);
    }

    [Fact]
    public void ConcurrentSessionChangesHaveDatabaseGeneratedRowVersion()
    {
        using var db = Context();
        var token = db.Model.FindEntityType(typeof(InterviewSession))!.FindProperty(nameof(InterviewSession.RowVersion))!;
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);
    }

    [Fact]
    public void RepeatedSubmissionsTurnsAndMembershipsAreConstrained()
    {
        using var db = Context();
        void UniqueIndex<T>(params string[] names) => Assert.Contains(db.Model.FindEntityType(typeof(T))!.GetIndexes(),
            x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(names));
        UniqueIndex<InterviewTurn>("SessionId", "SequenceNumber");
        UniqueIndex<TurnScore>("TurnId", "RubricCriterionId");
        UniqueIndex<AssignmentSubmission>("SessionId");
        UniqueIndex<AssignmentSubmission>("AssignmentId", "StudentId", "AttemptNumber");
        UniqueIndex<Classroom>("JoinCode");
        Assert.Equal(new[] { "ClassId", "StudentId" }, db.Model.FindEntityType(typeof(ClassMember))!.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(new[] { "QuestionBankId", "SkillTagId" }, db.Model.FindEntityType(typeof(QuestionSkillTag))!.FindPrimaryKey()!.Properties.Select(x => x.Name));
    }

    [Fact]
    public void BothFollowUpRelationshipsReferenceTurnsAndRemainOptional()
    {
        using var db = Context();
        var foreignKeys = db.Model.FindEntityType(typeof(InterviewTurn))!.GetForeignKeys()
            .Where(x => x.PrincipalEntityType.ClrType == typeof(InterviewTurn)).ToArray();
        Assert.Equal(2, foreignKeys.Length);
        Assert.All(foreignKeys, x => Assert.False(x.IsRequired));
        Assert.Equal(new[] { "ParentTurnId", "RootTurnId" }, foreignKeys.SelectMany(x => x.Properties).Select(x => x.Name).OrderBy(x => x));
    }

    [Fact]
    public void CostCanRemainUnknownAndRubricActivationIsUnique()
    {
        using var db = Context();
        var cost = db.Model.FindEntityType(typeof(AiRequestLog))!.FindProperty(nameof(AiRequestLog.EstimatedCostUsd))!;
        Assert.True(cost.IsNullable);
        Assert.Equal(18, cost.GetPrecision());
        Assert.Equal(8, cost.GetScale());
        var active = Assert.Single(db.Model.FindEntityType(typeof(RubricVersion))!.GetIndexes(),
            x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "IsActive" }));
        Assert.True(active.IsUnique);
        Assert.Equal("[IsActive] = 1", active.GetFilter());
    }
}
