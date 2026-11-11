using System.Text.Json;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using Xunit;

namespace AiInterview.Tests;

public class QuestionPlanningTests
{
    [Fact]
    public void QaPlanCoversTenDistinctRoleRelevantTargetsAndSurvivesRetry()
    {
        var session = new InterviewSession { Id=Guid.Parse("8a103e34-8437-40e2-bad9-ae11fc4e8cf2"), Position=new Position{Name="QA/Tester"}, Kind=InterviewKind.Technical };
        var targets=Enumerable.Range(0,10).Select(n=>QuestionPlanning.Next(session,n)).ToArray();
        Assert.Equal(10,targets.Select(x=>x.Topic).Distinct().Count());
        Assert.Contains(targets,x=>x.Topic.Contains("boundary value"));
        Assert.Contains(targets,x=>x.Topic.Contains("regression"));
        Assert.Contains(targets,x=>x.Topic.Contains("REST API testing"));
        Assert.DoesNotContain(targets,x=>x.Topic.Contains("object-oriented"));
        Assert.Equal(targets[3],QuestionPlanning.Next(session,3));
    }
    [Fact]
    public void NewSessionsVaryTopicOrderWithoutLosingCoverage()
    {
        var orders=Enumerable.Range(0,12).Select(_=>new InterviewSession{Id=Guid.NewGuid(),Position=new Position{Name="QA/Tester"}})
            .Select(s=>string.Join('|',Enumerable.Range(0,5).Select(n=>QuestionPlanning.Next(s,n).Topic))).ToArray();
        Assert.True(orders.Distinct().Count()>1);
    }
    [Fact]
    public void DuplicateCheckIgnoresAccentCaseAndFormattingButAllowsDistinctQuestions()
    {
        string[] history=["Bạn kiểm tra API đăng nhập như thế nào khi mật khẩu không chính xác?"];
        Assert.True(QuestionPlanning.IsNearDuplicate("BAN KIEM TRA API DANG NHAP NHU THE NAO KHI MAT KHAU KHONG CHINH XAC!",history));
        Assert.True(QuestionPlanning.IsNearDuplicate("Bạn kiểm tra API đăng nhập như thế nào khi mật khẩu không chính xác? Hãy giải thích.",history));
        Assert.False(QuestionPlanning.IsNearDuplicate("Bạn phân biệt mức độ nghiêm trọng và độ ưu tiên của một lỗi như thế nào?",history));
    }
    [Fact]
    public void QuestionSchemaPreventsPrematureGoodbyeAndUnrequestedCoding()
    {
        var root=JsonSerializer.SerializeToElement(AiPrompts.QuestionSchema(false,false));
        var fields=root.GetProperty("properties");
        Assert.False(fields.GetProperty("finished").GetProperty("enum")[0].GetBoolean());
        Assert.False(fields.GetProperty("followUp").GetProperty("enum")[0].GetBoolean());
        Assert.False(fields.GetProperty("isCoding").GetProperty("enum")[0].GetBoolean());
        Assert.Equal("",fields.GetProperty("codeLanguage").GetProperty("enum")[0].GetString());
    }
}
