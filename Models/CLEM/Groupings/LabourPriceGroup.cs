﻿using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Models.CLEM.Groupings
{
    ///<summary>
    /// Contains a group of filters to identify individual labour in a set price group
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(LabourPricing))]
    [Description("Set the pay rate for the selected group of individuals")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Filters/Groups/LabourPriceGroup.htm")]
    public class LabourPriceGroup : FilterGroup<LabourType>
    {
        /// <summary>
        /// Pay rate
        /// </summary>
        [Description("Daily pay rate")]
        [Required, GreaterThanEqualValue(0)]
        public double Value { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        protected LabourPriceGroup()
        {
            base.ModelSummaryStyle = HTMLSummaryStyle.SubResource;
        }

        #region descriptive summary

        /// <summary>
        /// Provides the description of the model settings for summary (GetFullSummary)
        /// </summary>
        /// <param name="formatForParentControl">Use full verbose description</param>
        /// <returns></returns>
        public override string ModelSummary(bool formatForParentControl)
        {
            string html = "";
            if (!formatForParentControl)
            {
                html += "\r\n<div class=\"activityentry\">";
                html += "Pay ";
                if (Value.ToString() == "0")
                {
                    html += "<span class=\"errorlink\">NOT SET";
                }
                else
                {
                    html += "<span class=\"setvalue\">";
                    html += Value.ToString("#,0.##");
                }
                html += "</span> for a days work";
                html += "</div>";
            }
            return html;
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryInnerClosingTags(bool formatForParentControl)
        {
            string html = "";
            if (formatForParentControl)
            {
                if (Value.ToString() == "0")
                {
                    html += "</td><td><span class=\"errorlink\">NOT SET";
                }
                else
                {
                    html += "</td><td><span class=\"setvalue\">";
                    html += this.Value.ToString("#,0.##");
                }
                html += "</span></td>";
                html += "</tr>";
            }
            else
            {
                html += "\r\n</div>";
            }
            return html;
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryInnerOpeningTags(bool formatForParentControl)
        {
            string html = "";
            if (formatForParentControl)            
                html += "<tr><td>" + this.Name + "</td><td>";
            else            
                html += "\r\n<div class=\"filterborder clearfix\">";            

            if (FindAllChildren<Filter>().Count() < 1)
                html += "<div class=\"filter\">All individuals</div>";

            return html;
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryClosingTags(bool formatForParentControl)
        {
            return !formatForParentControl ? base.ModelSummaryClosingTags(true) : "";
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryOpeningTags(bool formatForParentControl)
        {
            return !formatForParentControl ? base.ModelSummaryOpeningTags(true) : "";
        } 
        #endregion
    }
}
