﻿using Models.CLEM.Interfaces;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Display = Models.Core.DisplayAttribute;

namespace Models.CLEM.Groupings
{
    ///<summary>
    /// A component to determine how many of the filtered group to use
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [Description("Defines the number of individuals to take")]
    [ValidParent(ParentType = typeof(IFilterGroup))]
    [Version(1, 0, 0, "")]
    public class TakeFromFiltered : CLEMModel, IValidatableObject
    {
        /// <summary>
        /// Take style
        /// </summary>
        [Description("Style")]
        [Required]
        public TakeFromFilterStyle TakeStyle { get; set; }

        /// <summary>
        /// Value to take
        /// </summary>
        [Description("Take")]
        [System.ComponentModel.DefaultValueAttribute(1.0f)]
        public float Value { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public TakeFromFiltered()
        {
            SetDefaults();
        }

        /// <summary>
        /// Metod to calculate the number required based on style and ppoulation zise
        /// </summary>
        /// <param name="groupSize">The number of individuals in the group</param>
        /// <returns>Number to take</returns>
        public int NumberToTake(int groupSize)
        {
            int numberToTake;
            if (TakeStyle == TakeFromFilterStyle.Individuals)
                numberToTake = Convert.ToInt32(Value);
            else
                numberToTake = Convert.ToInt32(Math.Ceiling(Value * groupSize));
            return Math.Min(numberToTake, groupSize);
        }

        /// <summary>
        /// Convert sort to string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return takeString(false);
        }

        /// <summary>
        /// Convert sort to html string
        /// </summary>
        /// <returns></returns>
        public string ToHtmlString()
        {
            return takeString(true);
        }

        private string takeString(bool htmltags)
        {
            using (StringWriter takeWriter = new StringWriter())
            {
                string cssSet = "";
                string cssError = "";
                string cssClose = "";
                if(htmltags)
                {
                    cssSet = "<span class = \"filterset\">";
                    cssError = "<span class = \"filtererror\">";
                    cssClose = "</span>";
                }

                takeWriter.Write($"Take: ");
                string errorString = "";
                if (Value < 0 || (TakeStyle == TakeFromFilterStyle.Proportion & Value > 1))
                    errorString = "Invalid";

                if (errorString != "")
                    takeWriter.Write($"{cssError}{errorString}{cssClose}");
                else
                {
                    takeWriter.Write(cssSet);
                    takeWriter.Write(((TakeStyle == TakeFromFilterStyle.Proportion) ? Value.ToString("P0") : $"{Convert.ToInt32(Value)}"));
                    takeWriter.Write(cssClose);
                    takeWriter.WriteLine(((TakeStyle == TakeFromFilterStyle.Proportion) ? "" : " individuals"));
                }
                return takeWriter.ToString();
            }
        }

        #region validation
        /// <inheritdoc/>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            if (Value == 0)
            {
                string[] memberNames = new string[] { "Invalid value to take from filter" };
                results.Add(new ValidationResult($"Provide a {((TakeStyle == TakeFromFilterStyle.Proportion)?"proportion":"number of individuals")} greater than 0 for [f={Name}] in [f={Parent.Name}]", memberNames));
            }

            if (TakeStyle == TakeFromFilterStyle.Proportion)
            {
                if (Value > 1)
                {
                    string[] memberNames = new string[] { "Invalid proportion to take from filter" };
                    results.Add(new ValidationResult($"The proportion to take from [f={Name}] in [f={Parent.Name}] must be less than or equal to 1", memberNames));
                }
            }
            return results;
        }
        #endregion

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary(bool formatForParentControl)
        {
            return $"<div class=\"filter\" style=\"opacity: {((Enabled) ? "1" : "0.4")}\">{ToHtmlString()}</div>";
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryClosingTags(bool formatForParentControl)
        {
            // allows for collapsed box and simple entry
            return "";
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryOpeningTags(bool formatForParentControl)
        {
            // allows for collapsed box and simple entry
            return "";
        }
        #endregion
    }

}
