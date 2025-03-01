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
using System.IO;
using Models.CLEM.Interfaces;

namespace Models.CLEM.Groupings
{
    ///<summary>
    /// Contains a group of filters to identify individual ruminants in a set price group
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(AnimalPricing))]
    [Description("Define the sale and purchase price for a specified group of individuals")]
    [Version(1, 0, 1, "")]
    [Version(1, 0, 2, "Purchase and sales identifier used")]
    [HelpUri(@"Content/Features/Filters/Groups/AnimalPriceGroup.htm")]
    public class AnimalPriceGroup : FilterGroup<Ruminant>, IResourcePricing, IReportPricingChange
    {
        /// <summary>
        /// Style of pricing animals
        /// </summary>
        [Description("Style of pricing animals")]
        [Required]
        public PricingStyleType PricingStyle { get; set; }

        /// <summary>
        /// Value of individual
        /// </summary>
        [Description("Value")]
        [Required, GreaterThanEqualValue(0)]
        public double Value { get; set; }

        /// <summary>
        /// Determine whether this is a purchase or sale price, or both
        /// </summary>
        [Description("Purchase or sale price")]
        [System.ComponentModel.DefaultValueAttribute(PurchaseOrSalePricingStyleType.Both)]
        public PurchaseOrSalePricingStyleType PurchaseOrSale { get; set; }

        /// <summary>
        /// Calulate the value of an individual
        /// </summary>
        /// <param name="ind"></param>
        /// <returns></returns>
        public double CalculateValue(object ind)
        {
            if(ind is Ruminant)
            {
                double multiplier = 1;
                switch (PricingStyle)
                {
                    case PricingStyleType.perHead:
                        break;
                    case PricingStyleType.perKg:
                        multiplier = (ind as Ruminant).Weight;
                        break;
                    case PricingStyleType.perAE:
                        multiplier = (ind as Ruminant).AdultEquivalent;
                        break;
                    default:
                        break;
                }

                return Value * multiplier;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public ResourcePriceChangeDetails LastPriceChange { get; set; }

        /// <inheritdoc/>
        public IResourceType Resource { get { return FindAncestor<IResourceType>(); } }

        /// <summary>
        /// Constructor
        /// </summary>
        protected AnimalPriceGroup()
        {
            base.ModelSummaryStyle = HTMLSummaryStyle.SubResource;
        }

        /// <inheritdoc/>
        public event EventHandler PriceChangeOccurred;

        /// <inheritdoc/>
        public void SetPrice(double amount)
        {
            Value = amount;
        }

        /// <inheritdoc/>
        public double CurrentPrice { get { return Value; } }

        /// <inheritdoc/>
        [JsonIgnore]
        public double PreviousPrice { get; set; }


        /// <inheritdoc/>
        public void SetPrice(double amount, IModel model)
        {
            PreviousPrice = CurrentPrice;
            Value = amount;

            if (LastPriceChange is null)
            {
                LastPriceChange = new ResourcePriceChangeDetails();
            }
            LastPriceChange.ChangedBy = model;
            LastPriceChange.PriceChanged = this;

            // price change event
            OnPriceChanged(new PriceChangeEventArgs() { Details = LastPriceChange });
        }

        /// <summary>
        /// Price changed event
        /// </summary>
        /// <param name="e"></param>
        protected void OnPriceChanged(PriceChangeEventArgs e)
        {
            PriceChangeOccurred?.Invoke(this, e);
        }

        #region descriptive summary

        /// <summary>
        /// Provides the description of the model settings for summary (GetFullSummary)
        /// </summary>
        /// <param name="formatForParentControl">Use full verbose description</param>
        /// <returns></returns>
        public override string ModelSummary(bool formatForParentControl)
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (!formatForParentControl)
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    switch (PurchaseOrSale)
                    {
                        case PurchaseOrSalePricingStyleType.Both:
                            htmlWriter.Write("Buy and sell for ");
                            break;
                        case PurchaseOrSalePricingStyleType.Purchase:
                            htmlWriter.Write("Buy for ");
                            break;
                        case PurchaseOrSalePricingStyleType.Sale:
                            htmlWriter.Write("Sell for ");
                            break;
                    }
                    if (Value.ToString() == "0")
                    {
                        htmlWriter.Write("<span class=\"errorlink\">NOT SET");
                    }
                    else
                    {
                        htmlWriter.Write("<span class=\"setvalue\">");
                        htmlWriter.Write(Value.ToString("#,0.##"));
                    }
                    htmlWriter.Write("</span> ");
                    htmlWriter.Write("<span class=\"setvalue\">");
                    htmlWriter.Write(PricingStyle.ToString());
                    htmlWriter.Write("</span>");
                    htmlWriter.Write("</div>");
                }
                return htmlWriter.ToString(); 
            }
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryInnerClosingTags(bool formatForParentControl)
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (formatForParentControl)
                {
                    if (Value.ToString() == "0")
                    {
                        htmlWriter.Write("</td><td><span class=\"errorlink\">NOT SET");
                    }
                    else
                    {
                        htmlWriter.Write("</td><td><span class=\"setvalue\">");
                        htmlWriter.Write(this.Value.ToString("#,0.##"));
                    }
                    htmlWriter.Write("</span></td>");
                    htmlWriter.Write("<td><span class=\"setvalue\">" + this.PricingStyle.ToString() + "</span></td>");
                    string buySellString = "";
                    switch (PurchaseOrSale)
                    {
                        case PurchaseOrSalePricingStyleType.Both:
                            buySellString = "Buy and sell";
                            break;
                        case PurchaseOrSalePricingStyleType.Purchase:
                            buySellString = "Buy";
                            break;
                        case PurchaseOrSalePricingStyleType.Sale:
                            buySellString = "Sell";
                            break;
                    }
                    htmlWriter.Write("<td><span class=\"setvalue\">" + buySellString + "</span></td>");
                    htmlWriter.Write("</tr>");
                }
                else
                {
                    htmlWriter.Write("\r\n</div>");
                }
                return htmlWriter.ToString(); 
            }
        }

        /// <summary>
        /// Provides the closing html tags for object
        /// </summary>
        /// <returns></returns>
        public override string ModelSummaryInnerOpeningTags(bool formatForParentControl)
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (formatForParentControl)
                {
                    htmlWriter.Write("<tr><td>" + this.Name + "</td><td>");
                    if (!(this.FindAllChildren<Filter>().Count() >= 1))
                    {
                        htmlWriter.Write("<div class=\"filter\">All individuals</div>");
                    }
                }
                else
                {
                    htmlWriter.Write("\r\n<div class=\"filterborder clearfix\">");
                    if (!(this.FindAllChildren<Filter>().Count() >= 1))
                    {
                        htmlWriter.Write("<div class=\"filter\">All individuals</div>");
                    }
                }
                return htmlWriter.ToString(); 
            }
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
