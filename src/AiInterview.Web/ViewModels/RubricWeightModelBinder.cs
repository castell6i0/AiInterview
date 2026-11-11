using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AiInterview.Web.ViewModels;

/// <summary>HTML number inputs post dot decimals even when the UI culture is vi-VN.</summary>
public sealed class RubricWeightModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var text = (value.FirstValue ?? "").Trim();
        // Accept a single decimal separator; never interpret separators as thousands.
        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var weight))
            bindingContext.Result = ModelBindingResult.Success(weight);
        else bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Trọng số phải là số, ví dụ 12.5 hoặc 12,5.");
        return Task.CompletedTask;
    }
}
