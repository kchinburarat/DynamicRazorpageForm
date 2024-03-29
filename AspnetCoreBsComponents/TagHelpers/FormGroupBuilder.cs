﻿using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.IO;
using System.Text.Encodings.Web;
using System.Linq;
using Microsoft.AspNetCore.Html;
using System.Reflection;

namespace AspnetCoreBsComponents.TagHelpers
{
    public class FormGroupBuilder
    {
        private readonly IHtmlGenerator _htmlGenerator;
        private readonly ViewContext _viewContext;
        private readonly HtmlEncoder _htmlEncoder;
        private readonly IHtmlHelper _htmlHelper;

        public FormGroupBuilder(IHtmlGenerator htmlGenerator, 
            ViewContext viewContext, 
            HtmlEncoder htmlEncoder,
            IHtmlHelper htmlHelper)
        {
            _htmlGenerator = htmlGenerator;
            _viewContext = viewContext;
            _htmlEncoder = htmlEncoder;
            _htmlHelper = htmlHelper;
            (_htmlHelper as IViewContextAware).Contextualize(this._viewContext);
        }

        public Task<string> GetFormGroup(ModelExplorer property, TweakingConfiguration tweakingConfig)
        {
            if (!property.IsReadOnly())
            {
                ItemsSourceAttribute itemSource;
                if (property.HasValueFromSelectList(out itemSource))
                {
                    return _getFormGroupForSelectList(property, itemSource.GetItems(property.Container),
                        itemSource.ChoicesType, tweakingConfig);
                }
                else if (property.ModelType.IsSimpleType())
                {
                    return _getFormGroupForSimpleProperty(property, tweakingConfig);
                }
                else
                {
                    return _getFormGroupsForComplexProperty(property, tweakingConfig);
                }
            }
            else
            {
                return Task.Run(() => String.Empty);
            }
        }

        private async Task<string> _getFormGroupForSelectList(ModelExplorer property, IEnumerable<SelectListItem> items, 
            ChoicesTypes choicesType, TweakingConfiguration tweakingConfig)
        {
            string label = await buildLabelHtml(property, tweakingConfig);

            string select = "";
            if (choicesType == ChoicesTypes.RADIO)
            {
                select = await buildRadioInputsHtml(property, items, tweakingConfig);
            }
            else
            {
                select = await buildSelectHtml(property, items, tweakingConfig);
            }
            
            string validation = await buildValidationMessageHtml(property, tweakingConfig);
            return $@"<div class='form-group'>
                {label}
                {select}
                {validation}
</div>";
        }

        private async Task<string> _getFormGroupForSimpleProperty(ModelExplorer property,
            TweakingConfiguration tweakingConfig)
        {
            string label = await buildLabelHtml(property, tweakingConfig);

            string input = await buildInputHtml(property, tweakingConfig);

            string validation = await buildValidationMessageHtml(property, tweakingConfig);
            return $@"<div class='form-group'>
                {label}
                {input}
                {validation}
</div>";
        }

        private async Task<string> _getFormGroupsForComplexProperty(ModelExplorer property,
            TweakingConfiguration config)
        {
            StringBuilder builder = new StringBuilder();

            string label = await buildLabelHtml(property, config);
            foreach (var prop in property.Properties)
            {
                builder.Append(await GetFormGroup(prop, config));
            }

            return $@"<div class='form-group'>
                    {label}
                    <div class=""sub-form-group"">
                        {builder.ToString()}
                    </div>
</div>";
        }
        private async Task<string> buildLabelHtml(ModelExplorer property, TweakingConfiguration tweakingConfig)
        {
            TagHelper label = new LabelTagHelper(_htmlGenerator)
            {
                For = new ModelExpression(property.GetFullName(), property),
                ViewContext = _viewContext
            };

            PropertyTweakingConfiguration propertyConfig = tweakingConfig
                .GetByPropertyFullName(property.GetFullName());
            return await GetGeneratedContentFromTagHelper(
                "label",
                TagMode.StartTagAndEndTag, label,
                new TagHelperAttributeList() {
                    new TagHelperAttribute("class", propertyConfig?.LabelClasses)
                });
        }

        private async Task<string> buildRadioInputsHtml(ModelExplorer property, 
            IEnumerable<SelectListItem> items, 
            TweakingConfiguration tweakingConfig)
        {
            StringBuilder inputs = new StringBuilder();
            foreach (var item in items)
            {
                inputs.Append($"<br>{await buildInputHtml(property, tweakingConfig, "radio", item.Value, item.Selected)}&nbsp;<span>{item.Text}</span>");
            }
            return inputs.ToString();
        }
        
        private async Task<string> buildSelectHtml(ModelExplorer property, IEnumerable<SelectListItem> items, TweakingConfiguration tweakingConfig)
        {
            TagHelper select = new SelectTagHelper(_htmlGenerator)
            {
                For = new ModelExpression(property.GetFullName(), property),
                ViewContext = _viewContext,
                Items = items
            };

            return await GetGeneratedContentFromTagHelper("select",
                TagMode.StartTagAndEndTag,
                select,
                new TagHelperAttributeList { new TagHelperAttribute("class", "form-control dropdown") });
        }

        

        private async Task<string> buildInputHtml(ModelExplorer property, TweakingConfiguration tweakingConfig, 
            string inputType="", string inputValue="", bool radioChecked = false)
        {
            PropertyTweakingConfiguration propertyConfig = tweakingConfig.GetByPropertyFullName(property.GetFullName());
            if (propertyConfig == null || string.IsNullOrEmpty(propertyConfig.InputTemplatePath))
            {
                InputTagHelper input = new InputTagHelper(_htmlGenerator)
                {
                    For = new ModelExpression(property.GetFullName(), property),
                    ViewContext = _viewContext
                };
                var attrs = new TagHelperAttributeList { new TagHelperAttribute("class", $"form-control {propertyConfig?.InputClasses}") };

                if (!string.IsNullOrEmpty(inputType))
                {
                    input.InputTypeName = inputType;
                    input.Value = inputValue;
                    // Setting the Type attributes requires providing an initialized
                    // AttributeList with the type attribute
                    attrs = new TagHelperAttributeList()
                    {
                        new TagHelperAttribute("class", $"{propertyConfig?.InputClasses}"),
                        new TagHelperAttribute("type", inputType),
                        new TagHelperAttribute("value", inputValue)
                    };
                    if (radioChecked)
                    {
                        attrs.Add(new TagHelperAttribute("checked", "checked"));
                    }
                }

                AutoCompleteAttribute autoComplete;
                if (property.HasAutoComplete(out autoComplete))
                {
                    attrs.AddClass("autocomplete");
                    attrs.Add(getAutoCompleteDataAttribute(autoComplete, property.Container));
                }

                return await GetGeneratedContentFromTagHelper("input",
                    TagMode.SelfClosing,
                    input,
                    attributes: attrs
                    );
            }
            else
            {
                return renderInputTemplate(property, propertyConfig.InputTemplatePath);
            }
        }

        private TagHelperAttribute getAutoCompleteDataAttribute(AutoCompleteAttribute autoComplete, ModelExplorer parentModel)
        {
            if (!string.IsNullOrEmpty(autoComplete.SuggestionsProperty))
            {
                string suggestions = autoComplete.GetSuggestionsAsJson(parentModel);
                return new TagHelperAttribute("data-source-local", suggestions);
            }
            else if (!string.IsNullOrEmpty(autoComplete.SuggestionsEndpoint))
            {
                return new TagHelperAttribute("data-source-ajax", autoComplete.SuggestionsEndpoint);
            }
            return null;
        }
        private async Task<string> buildValidationMessageHtml(ModelExplorer property, TweakingConfiguration tweakingConfig)
        {
            PropertyTweakingConfiguration propertyConfig = tweakingConfig.GetByPropertyFullName(property.GetFullName());

            TagHelper validationMessage = new ValidationMessageTagHelper(_htmlGenerator)
            {
                For = new ModelExpression(property.GetFullName(), property),
                ViewContext = _viewContext
            };

            var errorClassName = propertyConfig?.ValidationClasses;
            if (string.IsNullOrEmpty(errorClassName)) errorClassName = "text-danger";

            return await GetGeneratedContentFromTagHelper("span",
                TagMode.StartTagAndEndTag,
                validationMessage,
                new TagHelperAttributeList() { new TagHelperAttribute("class", propertyConfig?.ValidationClasses) });
        }

        private async Task<string> GetGeneratedContentFromTagHelper(string tagName, TagMode tagMode,
            ITagHelper tagHelper, TagHelperAttributeList attributes = null)
        {
            if (attributes == null)
            {
                attributes = new TagHelperAttributeList();
            }

            TagHelperOutput output = new TagHelperOutput(tagName, attributes, (arg1, arg2) =>
            {
                return Task.Run<TagHelperContent>(() => new DefaultTagHelperContent());
            })
            {
                TagMode = tagMode
            };
            TagHelperContext context = new TagHelperContext(attributes, new Dictionary<object, object>(), Guid.NewGuid().ToString());

            tagHelper.Init(context);
            await tagHelper.ProcessAsync(context, output);

            return output.RenderTag(_htmlEncoder);
        }

        private string renderInputTemplate(ModelExplorer property, string path)
        {
            return _htmlHelper.Editor(property.Metadata.PropertyName, path).RenderTag(_htmlEncoder);
        }
    }

    #region Utility_Extension_Methods
    public static class Extensions
    {
        public static string GetFullName(this ModelExplorer property)
        {
            List<string> nameComponents = new List<String> { property.Metadata.PropertyName };

            while (!string.IsNullOrEmpty(property.Container.Metadata.PropertyName))
            {
                nameComponents.Add(property.Container.Metadata.PropertyName);
                property = property.Container;
            }

            nameComponents.Reverse();
            return string.Join(".", nameComponents);
        }
        public static bool IsSimpleType(this Type propertyType)
        {
            Type[] simpleTypes = new Type[]
            {
                typeof(string), typeof(bool), typeof(byte), typeof(char), typeof(DateTime), typeof(DateTimeOffset),
                typeof(decimal), typeof(double), typeof(Guid), typeof(short), typeof(int), typeof(long), typeof(float),
                typeof(TimeSpan), typeof(ushort), typeof(uint),typeof(ulong)
            };
            if (propertyType.IsConstructedGenericType && propertyType.Name.Equals("Nullable`1"))
            {
                return IsSimpleType(propertyType.GenericTypeArguments.First());
            }
            else
            {
                return (!propertyType.IsArray && !propertyType.IsPointer && simpleTypes.Contains(propertyType));
            }
        }
        public static string RenderTag(this IHtmlContent output, HtmlEncoder encoder)
        {
            using (var writer = new StringWriter())
            {
                output.WriteTo(writer, encoder);
                return writer.ToString();
            }
        }
        public static bool HasValueFromSelectList(this ModelExplorer property, out ItemsSourceAttribute itemSource)
        {
            return property.HasAttribute<ItemsSourceAttribute>(out itemSource);
        }

        public static bool HasAutoComplete(this ModelExplorer property, out AutoCompleteAttribute autoComplete)
        {
            return property.HasAttribute<AutoCompleteAttribute>(out autoComplete);
        }

        public static bool HasAttribute<T>(this ModelExplorer property, out T attribute) where T : Attribute
        {
            attribute = property.Container.ModelType.GetTypeInfo()
                    .GetProperty(property.Metadata.PropertyName)
                    .GetCustomAttribute<T>();

            return (attribute != null);
        }
        public static bool IsReadOnly(this ModelExplorer property)
        {
            return property.Metadata.PropertySetter == null;
        }

        public static void AddClass(this TagHelperAttributeList attrList, string cssClass)
        {
            var attrs = attrList.Where(a => a.Name.Equals("class"));
            if (attrs.Count() == 1)
            {
                TagHelperAttribute classAttr = attrs.First();
                attrList.Remove(classAttr);
                attrList.Add(new TagHelperAttribute("class", $"{classAttr.Value} autocomplete"));
            }
        }
    }
    #endregion
}
