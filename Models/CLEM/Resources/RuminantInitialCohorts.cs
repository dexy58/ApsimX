﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Models.CLEM.Activities;
using Models.Core;
using Models.Core.Attributes;
using Models.CLEM.Interfaces;

namespace Models.CLEM.Resources
{
    /// <summary>
    /// Holder for all initial ruminant cohorts
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyMultiModelView")]
    [PresenterName("UserInterface.Presenters.PropertyMultiModelPresenter")]
    [ValidParent(ParentType = typeof(RuminantType))]
    [Description("Holds the list of initial cohorts for a given ruminant herd")]
    [Version(1, 0, 2, "Includes attribute specification for whole herd")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Resources/Ruminants/RuminantInitialCohorts.htm")]
    public class RuminantInitialCohorts : CLEMModel
    {
        /// <summary>
        /// Records if a warning about set weight occurred
        /// </summary>
        public bool WeightWarningOccurred = false;

        /// <summary>
        /// Constructor
        /// </summary>
        protected RuminantInitialCohorts()
        {
            base.ModelSummaryStyle = HTMLSummaryStyle.SubResourceLevel2;
        }

        /// <summary>
        /// Create the individual ruminant animals for this Ruminant Type (Breed)
        /// </summary>
        /// <returns>A list of ruminants</returns>
        public List<Ruminant> CreateIndividuals()
        {
            List<ISetAttribute> initialCohortAttributes = this.FindAllChildren<ISetAttribute>().ToList();
            List<Ruminant> individuals = new List<Ruminant>();
            foreach (RuminantTypeCohort cohort in this.FindAllChildren<RuminantTypeCohort>())
                individuals.AddRange(cohort.CreateIndividuals(initialCohortAttributes));

            return individuals;
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary(bool formatForParentControl)
        {
            string html = "";
            return html;
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerClosingTags(bool formatForParentControl)
        {
            string html = "</table>";
            if(WeightWarningOccurred)
                html += "</br><span class=\"errorlink\">Warning: Initial weight differs from the expected normalised weight by more than 20%</span>";
            return html;
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerOpeningTags(bool formatForParentControl)
        {
            WeightWarningOccurred = false;
            return "<table><tr><th>Name</th><th>Sex</th><th>Age</th><th>Weight</th><th>Norm.Wt.</th><th>Number</th><th>Suckling</th><th>Sire</th></tr>";
        }

        #endregion
    }
}



