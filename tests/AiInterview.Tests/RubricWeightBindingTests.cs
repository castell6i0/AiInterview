using System.Globalization;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AiInterview.Tests;

public sealed class RubricWeightBindingTests
{
    [Theory]
    [InlineData("vi-VN", "12.5", "12.5")]
    [InlineData("vi-VN", "12,5", "12.5")]
    [InlineData("en-US", "12.5", "12.5")]
    [InlineData("vi-VN", "0.01", "0.01")]
    [InlineData("vi-VN", "100", "100")]
    public async Task HtmlNumberInputPreservesActualWeightAcrossCultures(string culture, string input, string expected)
    {
        var context = CreateContext(culture, input);
        await new RubricWeightModelBinder().BindModelAsync(context);
        Assert.True(context.Result.IsModelSet);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Theory]
    [InlineData("1,000.5")]
    [InlineData("1.000,5")]
    [InlineData("not a number")]
    [InlineData("")]
    public async Task AmbiguousOrInvalidWeightIsRejected(string input)
    {
        var context = CreateContext("vi-VN", input);
        await new RubricWeightModelBinder().BindModelAsync(context);
        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    private static ModelBindingContext CreateContext(string culture, string value)
    {
        var form = new FormCollection(new Dictionary<string, StringValues> { ["Criteria[0].Weight"] = value });
        return DefaultModelBindingContext.CreateBindingContext(new ActionContext(new DefaultHttpContext(), new(), new()),
            new FormValueProvider(BindingSource.Form, form, CultureInfo.GetCultureInfo(culture)),
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal)), null, "Criteria[0].Weight");
    }
}
