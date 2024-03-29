﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;

namespace AspnetCoreBsComponents.TagHelpers
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public class AutoCompleteAttribute : Attribute
    {
        public string SuggestionsProperty { get; set; }

        public string SuggestionsEndpoint { get; set; }

        public string GetSuggestionsAsJson(ModelExplorer modelExplorer)
        {
            var properties = modelExplorer.Properties
                .Where(p => p.Metadata.PropertyName.Equals(SuggestionsProperty));
            if (properties.Count() == 1)
            {
                return JsonConvert.SerializeObject(properties.First().Model as IEnumerable<string>);
            }
            return string.Empty;
        }
    }
}
