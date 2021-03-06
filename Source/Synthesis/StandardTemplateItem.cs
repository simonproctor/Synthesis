﻿using System;
using System.Linq;
using System.Reflection;
using Sitecore;
using Sitecore.ContentSearch;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Links;
using Synthesis.FieldTypes.Adapters;
using Synthesis.Utility;
using Sitecore.Configuration;
using System.Collections.Generic;
using Sitecore.Exceptions;
using Sitecore.Diagnostics;
using System.Collections.ObjectModel;
using Sitecore.Data.Managers;

namespace Synthesis
{
	/// <summary>
	/// The base class for all generated strongly typed items; an analogue to the Standard Template in Sitecore.
	/// </summary>
	/// <remarks>
	/// As a best practice refer to IStandardTemplateItem instead of this
	/// </remarks>
	public class StandardTemplateItem : IStandardTemplateItem
	{
		Item _innerItem;
		readonly IReadOnlyDictionary<string, string> _searchFields;
		private string _url;
		private ItemUri _searchUri;

		public StandardTemplateItem()
		{
			throw new NotImplementedException("This is here to satisfy a generic constraint in the Sitecore API. Don't use it.");
		}

		public StandardTemplateItem(Item item)
		{
			Assert.IsNotNull(item, "Item must not be null.");

			_innerItem = item;
		}

		public StandardTemplateItem(IDictionary<string, string> searchFields)
		{
			Assert.IsNotNull(searchFields, "Search fields must not be null.");

			_searchFields = new ReadOnlyDictionary<string, string>(searchFields);
		}

		/// <summary>
		/// The inner Item (Sitecore API class) that backs this item
		/// </summary>
		public Item InnerItem
		{
			get
			{
				Assert.IsNotNull(Uri, "uri");
				if (_innerItem == null)
				{

					System.Diagnostics.Debug.WriteLine("Synthesis: {0} ({1}) instance promoted from search to database item.", Name, Id);

					_innerItem = Sitecore.Data.Database.GetItem(Uri);
					if (_innerItem == null) throw new InvalidItemException("The item URI " + Uri + " did not result in a usable item. Couldn't ensure the item was loaded.");
				}

				return _innerItem;
			}
		}

		/// <summary>
		/// The unique Item URI that defines the item, version, and language this instance represents
		/// </summary>
		[IndexField("_uniqueid")]
		public ItemUri Uri
		{
			get
			{
				if (InstanceType == InstanceType.Database)
					return _innerItem.Uri;

				if (_searchUri == null)
				{
					string indexValue = GetSearchFieldValue("_uniqueid");

					Assert.IsNotNull(indexValue, "Couldn't get an ItemUri from the search fields!");

					_searchUri = ItemUri.Parse(indexValue);
				}

				return _searchUri;
			}
		}

		/// <summary>
		/// ID of the item
		/// </summary>
		[IndexField("_group")]
		public ID Id { get { return Uri.ItemID; } }

		/// <summary>
		/// Name of the item
		/// </summary>
		[IndexField("_name")]
		public virtual string Name
		{
			get
			{
				if (InstanceType == InstanceType.Search)
					return GetSearchFieldValue("_name") ?? InnerItem.Name;

				return InnerItem.Name;
			}
			set
			{
				using (new SingleFieldEditor(InnerItem))
				{
					InnerItem.Name = value;
				}
			}
		}

		/// <summary>
		/// Display name of the item (falls back to Name if Display Name is not present)
		/// Loads the Sitecore item if this is a search-driven instance
		/// </summary>
		[IndexField("__display_name")]
		public virtual string DisplayName
		{
			get
			{
				if (InstanceType == InstanceType.Search)
				{
					// NOTE: this field is not part of the index by default; you will need to enable it for resolution using the index
					var indexDisplayName = GetSearchFieldValue("__display_name");

					if (indexDisplayName == null) // field was not present, use db display name
						return InnerItem.DisplayName;

					// display name is in index but was blank (e.g. name is used, no different display name)
					if (indexDisplayName == string.Empty)
						indexDisplayName = GetSearchFieldValue("_name");

					if (indexDisplayName != null)
						return indexDisplayName;
				}

				return InnerItem.DisplayName;
			}
			set
			{
				using (new SingleFieldEditor(InnerItem))
				{
					InnerItem[FieldIDs.DisplayName] = value;
				}
			}
		}

		/// <summary>
		/// Item Template ID.
		/// </summary>
		/// <remarks>Overridden by generated classes to be hard-coded</remarks>
		[IndexField("_template")]
		public virtual ID TemplateId
		{
			get
			{
				if (InstanceType == InstanceType.Search)
				{
					var searchTemplate = GetSearchFieldValue("_template");
					ShortID templateId;

					if (searchTemplate != null && ShortID.TryParse(searchTemplate, out templateId))
						return templateId.ToID();
				}

				return InnerItem.TemplateID;
			}
		}

		/// <summary>
		/// Gets all base templates that this item's template inherits from
		/// </summary>
		[IndexField("_templatesimplemented")]
		public virtual ID[] TemplateIds
		{
			get
			{
				if (InstanceType == InstanceType.Search)
				{
					var searchTemplate = GetSearchFieldValue("_templatesimplemented");

					// TODO: this looks busted?
					return new ID[0];
				}

				return TemplateManager.GetTemplate(InnerItem.TemplateID, InnerItem.Database)
									.GetBaseTemplates()
									.Select(x => x.ID)
									.ToArray();
			}
		}

		/// <summary>
		/// The database this item resides in
		/// </summary>
		public IDatabaseAdapter Database
		{
			get
			{
				return new DatabaseAdapter(Factory.GetDatabase(Uri.DatabaseName));
			}
		}

		/// <summary>
		/// Item version number
		/// </summary>
		[IndexField("_version")]
		public virtual int Version
		{
			get { return Uri.Version.Number; }
		}

		/// <summary>
		/// Gets if the item backing this instance is the latest version in its language
		/// </summary>
		[IndexField("_latestversion")]
		public bool IsLatestVersion
		{
			get
			{
				if (InstanceType == InstanceType.Search)
				{
					return GetSearchFieldValue("_latestversion") != null;
				}

				return InnerItem.Versions.IsLatestVersion();
			}
		}

		/// <summary>
		/// Item language.
		/// </summary>
		[IndexField("_language")]
		public virtual Language Language
		{
			get { return Uri.Language; }
		}

		/// <summary>
		/// Item statistics, i.e. created date
		/// Loads the Sitecore item if this is a search-driven instance
		/// </summary>
		public virtual IStatisticsAdapter Statistics
		{
			get { return new StatisticsAdapter(InnerItem.Statistics); }
		}

		/// <summary>
		/// Gets a strongly typed version of the item's axes for relative querying
		/// </summary>
		public virtual IAxesAdapter Axes
		{
			get { return new AxesAdapter(InnerItem); }
		}

		/// <summary>
		/// Source path data for the item
		/// </summary>
		public virtual IPathAdapter Paths
		{
			get { return new PathAdapter(InnerItem.Paths); }
		}

		/// <summary>
		/// Provides access to the publishing framework
		/// Loads the Sitecore item if this is a search-driven instance
		/// </summary>
		public virtual IEditingAdapter Editing
		{
			get { return new EditingAdapter(InnerItem.Editing); }
		}

		/// <summary>
		/// Gets the URL to this item using the default LinkManager options. Returns null if not yet created.
		/// </summary>
		public virtual string Url
		{
			get
			{
				if (InnerItem == null) return null;

				if (_url == null)
				{
					if (Paths.IsMediaItem)
						_url = Sitecore.Resources.Media.MediaManager.GetMediaUrl(InnerItem);
					else
						_url = LinkManager.GetItemUrl(InnerItem);
				}
				return _url;
			}
			set { _url = value; }
		}

		/// <summary>
		/// Gets a Synthesis field object for a given field
		/// Loads the Sitecore item if this is a search-driven instance
		/// </summary>
		public virtual FieldDictionary Fields
		{
			get
			{
				return new FieldDictionary(this);
			}
		}

		/// <summary>
		/// Search field indexer. Provides raw access to index field values for search-backed instances.
		/// </summary>
		/// <param name="searchFieldName">The index field name to get the value of.</param>
		/// <returns>The field name, or null if it did not exist.</returns>
		public string this[string searchFieldName]
		{
			get
			{
				return GetSearchFieldValue(searchFieldName);
			}
		}

		/// <summary>
		/// Determines if this item instance is currently proxying a search index result or the Sitecore database
		/// </summary>
		public InstanceType InstanceType
		{
			get
			{
				return _innerItem == null ? InstanceType.Search : InstanceType.Database;
			}
		}

		/// <summary>
		/// Adds a new item as a child of this item
		/// </summary>
		/// <typeparam name="TItem">The Synthesis type of the child to add. Must be a concrete template type.</typeparam>
		/// <param name="name">Name of the new item</param>
		/// <returns>The newly added child item</returns>
		public TItem Add<TItem>(string name)
			where TItem : class, IStandardTemplateItem
		{
			var type = typeof (TItem);
			var property = type.GetProperty("ItemTemplateId", BindingFlags.Static | BindingFlags.Public);

			if(property == null) throw new ArgumentException(type.FullName + " does not seem to be a generated item type (no ItemTemplateId property was present)");

			var propertyValue = property.GetValue(null) as ID;

			if(propertyValue == (ID)null) throw new ArgumentException("ItemTemplateId property was not of the expected Sitecore.Data.ID type");

			Item newItem = InnerItem.Add(name, new TemplateID(propertyValue));

			return newItem.As<TItem>();
		}

		/// <summary>
		/// Creates an informational string about this item.
		/// </summary>
		public override string ToString()
		{
			return string.Format("{0} ({1} v{2}, {3}) - {4}", Name, Language.Name, Version, Database.Name, Id);
		}

		/// <summary>
		/// Gets a field value from the search fields this item is based on. Returns null if the field does not exist or is not castable to the expected type.
		/// </summary>
		protected virtual string GetSearchFieldValue(string fieldName)
		{
			if (_searchFields == null) return null;

			string result;

			if (!_searchFields.TryGetValue(fieldName, out result)) return null;

			return result;
		}
	}
}
