﻿using Models.CLEM.Activities;
using Models.CLEM.Interfaces;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Models.CLEM.Resources
{
    ///<summary>
    /// Resource request for Resource from a ResourceType
    ///</summary> 
    [Serializable]
    public class ResourceRequest
    {
        ///<summary>
        /// Link to resource being requested 
        ///</summary> 
        [JsonIgnore]
        public IResourceType Resource { get; set; }
        ///<summary>
        /// Type of resource being requested 
        ///</summary> 
        [JsonIgnore]
        [field: NonSerialized]
        public Type ResourceType { get; set; }
        ///<summary>
        /// Name of resource type being requested 
        ///</summary> 
        public string ResourceTypeName { get; set; }
        ///<summary>
        /// Name of activity requesting resource
        ///</summary> 
        public CLEMModel ActivityModel { get; set; }
        ///<summary>
        /// Unique identifier for instance of activity request
        /// Used to allow multiple concurrent resource requests i.e labour types.
        ///</summary> 
        public Guid ActivityID { get; set; }
        ///<summary>
        /// Category for requesting resource
        ///</summary> 
        public string Category { get; set; }
        ///<summary>
        /// Resource this transaction relates to (not uses)
        ///</summary> 
        public string RelatesToResource { get; set; }
        ///<summary>
        /// Amount required 
        ///</summary> 
        public double Required { get; set; }
        ///<summary>
        /// Amount available
        ///</summary> 
        public double Available { get; set; }
        ///<summary>
        /// Amount provided
        ///</summary> 
        public double Provided { get; set; }
        ///<summary>
        /// Value provided
        ///</summary> 
        public double Value { get; set; }
        ///<summary>
        /// Filtering and sorting items list
        ///</summary> 
        public List<object> FilterDetails { get; set; }
        ///<summary>
        /// Additional details for this request
        ///</summary> 
        public object AdditionalDetails { get; set; }
        ///<summary>
        /// Allow transmutation
        ///</summary> 
        public bool AllowTransmutation { get; set; }
        ///<summary>
        /// Successful transmutation
        ///</summary> 
        public Transmutation SuccessfulTransmutation { get; set; }
        ///<summary>
        /// Is Transmutation possible?
        ///</summary> 
        public bool TransmutationPossible { get { return (SuccessfulTransmutation != null); } }
        ///<summary>
        /// Market transcation multiplier
        /// 0 (default) = not a market transaction
        ///</summary> 
        public double MarketTransactionMultiplier { get; set; }

        ///<summary>
        /// ResourceRequest constructor
        ///</summary> 
        public ResourceRequest()
        {
            // default values
            SuccessfulTransmutation = null;
            AllowTransmutation = false;
        }
    }

    ///<summary>
    /// Additional information for animal food requests
    ///</summary> 
    public class FoodResourcePacket
    {
        ///<summary>
        /// Amount of food supplied
        ///</summary> 
        public double Amount { get; set; }
        ///<summary>
        /// Dry Matter Digestibility of food supplied
        ///</summary> 
        public double DMD { get; set; }
        ///<summary>
        /// Percent N of food supplied
        ///</summary> 
        public double PercentN { get; set; }
    }

    ///<summary>
    /// Information for a food parcel eaten
    ///</summary> 
    public class HumanFoodParcel
    {
        /// <summary>
        /// Link to the food store
        /// </summary>
        public HumanFoodStoreType FoodStore { get; set; }
        /// <summary>
        /// The pool of food
        /// </summary>
        public HumanFoodStorePool Pool { get; set; }
        /// <summary>
        /// Number of months before expires
        /// </summary>
        public int Expires { get; set; }
        /// <summary>
        /// Proportion eaten
        /// </summary>
        public double Proportion { get; set; }
    }

    /// <summary>
    /// Class for reporting transaction details in OnTransactionEvents
    /// </summary>
    [Serializable]
    public class ResourceRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Resource request details
        /// </summary>
        public ResourceRequest Request { get; set; }
    }

    /// <summary>
    /// Class for reporting last activity performed details in OnActivityPerformed
    /// </summary>
    [Serializable]
    public class ActivityPerformedEventArgs : EventArgs
    {
        /// <summary>
        /// Activity details
        /// </summary>
        public CLEMActivityBase Activity { get; set; }
    }


}
