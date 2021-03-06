using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Razor.Editor;
using System.Web.Razor.Parser.SyntaxTree;
using System.Web.Routing;
using System.Xml.Linq;
using BForms.Models;
using BForms.Mvc;
using BForms.Renderers;
using BForms.Utilities;

namespace BForms.Html
{
    public static class SortableListExtensions
    {

        public static BsSortableListHtmlBuilder<TOrderModel> BsSortableListFor<TOrderModel>(this HtmlHelper helper,
            IEnumerable<TOrderModel> model,
            Expression<Func<TOrderModel, BsSortableListConfiguration<TOrderModel>>> config,
            Expression<Func<TOrderModel, HtmlProperties>> listProperties,
            Expression<Func<TOrderModel, HtmlProperties>> itemProperties,
            Expression<Func<TOrderModel, HtmlProperties>> labelProperties,
            Expression<Func<TOrderModel, HtmlProperties>> badgeProperties)
        {
            if (config == null)
            {
                throw new Exception("Configuration expression cannot be null");
            }

            return new BsSortableListHtmlBuilder<TOrderModel>(model, helper.ViewContext, config, listProperties, itemProperties, labelProperties, badgeProperties);
        }

        public static BsSortableListHtmlBuilder<TOrderModel> BsSortableListFor<TOrderModel>(this HtmlHelper helper,
            IEnumerable<TOrderModel> model,
            Expression<Func<TOrderModel, BsSortableListConfiguration<TOrderModel>>> config)
        {
            return BsSortableListFor(helper, model, config, null, null, null, null);
        }

        public static BsSortableListHtmlBuilder<TOrderModel> BsSortableListFor<TOrderModel>(this HtmlHelper helper,
            IEnumerable<TOrderModel> model,
            Expression<Func<TOrderModel, BsSortableListConfiguration<TOrderModel>>> config,
            Expression<Func<TOrderModel, HtmlProperties>> listProperties)
        {
            return BsSortableListFor(helper, model, config, listProperties, null, null, null);
        }

        public static BsSortableListHtmlBuilder<TOrderModel> BsSortableListFor<TOrderModel>(this HtmlHelper helper,
            IEnumerable<TOrderModel> model,
            Expression<Func<TOrderModel, BsSortableListConfiguration<TOrderModel>>> config,
            Expression<Func<TOrderModel, HtmlProperties>> listProperties,
            Expression<Func<TOrderModel, HtmlProperties>> itemProperties)
        {
            return BsSortableListFor(helper, model, config, listProperties, itemProperties, null, null);
        }

    }

    public class SortableListItemWrapper
    {
        public TagBuilder LabelTag { get; set; }
        public TagBuilder ItemTag { get; set; }
        public TagBuilder BadgeTag { get; set; }

        /// <summary>
        /// @RootTag represents the tag which will wrap around any nested list
        /// </summary>
        public TagBuilder RootTag { get; set; }

        public List<SortableListItemWrapper> Children { get; set; }

        public SortableListItemWrapper(List<SortableListItemWrapper> children)
        {
            RootTag = new TagBuilder("ol");
            ItemTag = new TagBuilder("li");
            LabelTag = new TagBuilder("p");
            BadgeTag = new TagBuilder("span");

            Children = children;
        }

        public SortableListItemWrapper(TagBuilder root = null,
                                       TagBuilder item = null,
                                       TagBuilder label = null,
                                       TagBuilder badge = null,
                                       List<SortableListItemWrapper> children = null)
        {
            RootTag = root ?? new TagBuilder("ol");
            ItemTag = item ?? new TagBuilder("li");
            LabelTag = label ?? new TagBuilder("span");
            BadgeTag = badge ?? new TagBuilder("span");

            Children = children;
        }
    }

    public class HtmlProperties
    {
        public string Text { get; set; }
        public object HtmlAttributes { get; set; }
        public object DataAttributes { get; set; }

        public HtmlProperties()
        {
            Text = String.Empty;
            HtmlAttributes = new Dictionary<string, object>();
            DataAttributes = new Dictionary<string, object>();
        }

        public HtmlProperties(string text,
            IDictionary<string, object> htmlAttributes,
            IDictionary<string, object> dataAttributes)
        {
            Text = text;
            HtmlAttributes = htmlAttributes ?? new Dictionary<string, object>();
            DataAttributes = dataAttributes ?? new Dictionary<string, object>();
        }

        public HtmlProperties(string text, object htmlAttributes, object dataAttributes)
        {
            Text = text;
            HtmlAttributes = HtmlHelper.AnonymousObjectToHtmlAttributes(htmlAttributes);
            DataAttributes = HtmlHelper.AnonymousObjectToHtmlAttributes(dataAttributes);
        }
    }

    public class UnwrappedHtmlProperties
    {
        string Text { get; set; }
        public object HtmlAttributes { get; set; }
        public object DataAttributes { get; set; }
    }

    public class BsSortableListConfiguration<TModel>
    {
        public string Id { get; set; }
        public string Order { get; set; }
        public string Text { get; set; }

        // TODO : give this member a more sugestive name 
        public Expression<Func<TModel, bool>> AppendsTo { get; set; }
    }


    public class BsSortableListHtmlBuilder<TModel> : BsBaseComponent
    {
        public SortableListItemWrapper List { get; set; }
        public IEnumerable<TModel> Model { get; set; }
        private IEnumerable<TModel> _reducedTree;

        public string ParentPropertyName = "ParentId";

        private Dictionary<string, IEnumerable<string>> _permitedConnections;
        internal Dictionary<string, object> globalHtmlAttributes;
        internal Dictionary<string, object> globalDataAttributes;

        // Each of these expressions represents the last given configuration for a specific property
        // Storing them prevents erasing allready configured properties after each @List rebuilding
        private Expression<Func<TModel, BsSortableListConfiguration<TModel>>> _config;
        private Expression<Func<TModel, HtmlProperties>> _globalProperties;
        private Expression<Func<TModel, HtmlProperties>> _itemProperties;
        private Expression<Func<TModel, HtmlProperties>> _labelProperties;
        private Expression<Func<TModel, HtmlProperties>> _badgeProperties;
        private Expression<Func<TModel, HtmlProperties>> _emptyHtmlPropertiesExpression;

        public BsSortableListHtmlBuilder(IEnumerable<TModel> model,
            ViewContext viewContext,
            Expression<Func<TModel, BsSortableListConfiguration<TModel>>> config,
            Expression<Func<TModel, HtmlProperties>> globalProperties,
            Expression<Func<TModel, HtmlProperties>> itemProperties,
            Expression<Func<TModel, HtmlProperties>> labelProperties,
            Expression<Func<TModel, HtmlProperties>> badgeProperties) :
            base(viewContext)
        {
            Model = model;

            _emptyHtmlPropertiesExpression = x => new HtmlProperties();

            _reducedTree = GetReducedTree(model);

            List = Build(model, config, globalProperties, itemProperties, labelProperties, badgeProperties);

            _config = config;
            _globalProperties = globalProperties ?? _emptyHtmlPropertiesExpression;
            _itemProperties = itemProperties ?? _emptyHtmlPropertiesExpression;
            _labelProperties = labelProperties ?? _emptyHtmlPropertiesExpression;
            _badgeProperties = badgeProperties ?? _emptyHtmlPropertiesExpression;

            globalHtmlAttributes = new Dictionary<string, object>();
            globalDataAttributes = new Dictionary<string, object>() { { "migration-permited", true } };
        }

        #region Build & Render

        private SortableListItemWrapper Build(IEnumerable<TModel> modelList,
            Expression<Func<TModel, BsSortableListConfiguration<TModel>>> config,
            Expression<Func<TModel, HtmlProperties>> globalProperties,
            Expression<Func<TModel, HtmlProperties>> itemProperties,
            Expression<Func<TModel, HtmlProperties>> labelProperties,
            Expression<Func<TModel, HtmlProperties>> badgeProperties)
        {
            var sortableListItemWrapper = new SortableListItemWrapper();

            if (modelList != null && modelList.Any())
            {
                sortableListItemWrapper.Children = new List<SortableListItemWrapper>();

                _permitedConnections = GetPermitedConnections(_reducedTree, config);

                foreach (var item in modelList)
                {
                    sortableListItemWrapper.Children.Add(Build(item, config, globalProperties, itemProperties, labelProperties, badgeProperties));
                }
            }

            return sortableListItemWrapper;
        }

        private SortableListItemWrapper Build(TModel model,
            Expression<Func<TModel, BsSortableListConfiguration<TModel>>> config,
            Expression<Func<TModel, HtmlProperties>> globalProperties,
            Expression<Func<TModel, HtmlProperties>> itemProperties,
            Expression<Func<TModel, HtmlProperties>> labelProperties,
            Expression<Func<TModel, HtmlProperties>> badgeProperties)
        {
            var sortableListItemWrapper = new SortableListItemWrapper();

            #region Apply attributes

            var itemProps = itemProperties != null ? itemProperties.Compile().Invoke(model) : new HtmlProperties(null, null, null);
            var labelProps = labelProperties != null ? labelProperties.Compile().Invoke(model) : new HtmlProperties(null, null, null);
            var badgeProps = badgeProperties != null ? badgeProperties.Compile().Invoke(model) : new HtmlProperties(null, null, null);
            var globalProps = globalProperties != null ? globalProperties.Compile().Invoke(model) : new HtmlProperties(null, null, null);
            var configProps = config != null ? config.Compile().Invoke(model) : null;

            var htmlAttributes = new RouteValueDictionary();
            var dataAttributes = new RouteValueDictionary();

            #region Root

            sortableListItemWrapper.RootTag.MergeAttributes(DictionaryFromAttributes(globalProps.HtmlAttributes));
            sortableListItemWrapper.RootTag.MergeAttributes(DictionaryFromAttributes(globalProps.DataAttributes, "data-"));

            #endregion

            #region Item

            if (configProps != null)
            {
                var connectionString = String.Join(" ", _permitedConnections[configProps.Id].OrderBy(x => x));

                dataAttributes = DictionaryFromAttributes(itemProps.DataAttributes, "data-");

                dataAttributes.Add("data-id", configProps.Id);
                dataAttributes.Add("data-order", configProps.Order);
                dataAttributes.Add("data-appends-to", connectionString);
            }

            sortableListItemWrapper.ItemTag.MergeAttributes(DictionaryFromAttributes(itemProps.HtmlAttributes));
            sortableListItemWrapper.ItemTag.MergeAttributes(dataAttributes);

            #endregion

            #region Label

            htmlAttributes = DictionaryFromAttributes(labelProps.HtmlAttributes);


            sortableListItemWrapper.LabelTag.MergeAttributes(htmlAttributes);
            sortableListItemWrapper.LabelTag.MergeAttributes(DictionaryFromAttributes(labelProps.DataAttributes, "data-"));
            sortableListItemWrapper.LabelTag.InnerHtml = " " + (configProps != null ? configProps.Text : String.Empty);

            #endregion

            #region Badge

            sortableListItemWrapper.BadgeTag.MergeAttributes(DictionaryFromAttributes(badgeProps.HtmlAttributes));
            sortableListItemWrapper.BadgeTag.MergeAttributes(DictionaryFromAttributes(badgeProps.DataAttributes, "data-"));
            sortableListItemWrapper.BadgeTag.InnerHtml += badgeProps.Text;

            #endregion

            #endregion

            #region Build nested list

            var properties = model.GetType().GetProperties();
            PropertyInfo nestedValuesProperty = null;

            // Find the first property in @model's properties 
            // decorated with a BsControlAttribute matching BsControlType.SortableList
            foreach (PropertyInfo prop in properties)
            {
                if (nestedValuesProperty != null)
                {
                    break;
                }

                var attributes = prop.GetCustomAttributes(true);

                foreach (var attr in attributes)
                {
                    var bsControl = attr as BsControlAttribute;

                    if (bsControl != null && bsControl.ControlType == BsControlType.SortableList)
                    {
                        nestedValuesProperty = prop;
                        break;
                    }
                }
            }

            if (nestedValuesProperty != null)
            {
                var value = nestedValuesProperty.GetValue(model, null);
                sortableListItemWrapper.Children = new List<SortableListItemWrapper>();

                if (value != null && (value as IEnumerable<TModel>) != null)
                {

                    foreach (var item in (value as IEnumerable<TModel>))
                    {
                        sortableListItemWrapper.Children.Add(Build(item, config, globalProperties, itemProperties, labelProperties, badgeProperties));
                    }
                }

            }

            #endregion

            return sortableListItemWrapper;
        }

        #region Tree iteration & connection validations

        private Dictionary<string, IEnumerable<string>> GetPermitedConnections(IEnumerable<TModel> model, Expression<Func<TModel, BsSortableListConfiguration<TModel>>> config)
        {
            var connections = new Dictionary<string, IEnumerable<string>>();

            if (config == null)
            {
                return null;
            }

            foreach (var item in model)
            {
                var permitedConnections = new List<string>();
                var configValues = config.Compile().Invoke(item);
                var candidateItems = model.Where(x => !x.Equals(item));
                Expression<Func<TModel, bool>> validationExpression = configValues != null
                    ? configValues.AppendsTo
                    : null;

                var itemId = configValues != null ? configValues.Id : String.Empty;

                foreach (var candidateItem in candidateItems)
                {
                    if (IsValidConnection(item, candidateItem, validationExpression))
                    {
                        var candidateId = config.Compile().Invoke(candidateItem).Id;

                        permitedConnections.Add(candidateId);
                    }
                }

                connections.Add(itemId, permitedConnections);
            }

            return connections;
        }

        private IEnumerable<TModel> GetReducedTree(TModel model)
        {
            var items = new List<TModel>();

            var children = GetNestedTreeProperty(model);

            if (children != null)
            {
                items = items.Concat(GetReducedTree(children)).ToList();
            }

            return items;
        }

        private IEnumerable<TModel> GetReducedTree(IEnumerable<TModel> tree)
        {
            var items = new List<TModel>();


            if (tree != null && tree.Any())
            {
                foreach (var item in tree)
                {
                    items.Add(item);

                    var children = GetNestedTreeProperty(item);

                    if (children != null)
                    {
                        items = items.Concat(GetReducedTree(children)).ToList();
                    }
                }
            }

            return items;
        }

        private IEnumerable<TModel> GetNestedTreeProperty(TModel model)
        {
            var properties = model.GetType().GetProperties();
            PropertyInfo nestedValuesProperty = null;

            // Find the first property in @model's properties 
            // decorated with a BsControlAttribute matching BsControlType.SortableList
            foreach (PropertyInfo prop in properties)
            {
                if (nestedValuesProperty != null)
                {
                    break;
                }

                var attributes = prop.GetCustomAttributes(true);

                foreach (var attr in attributes)
                {
                    var bsControl = attr as BsControlAttribute;

                    if (bsControl != null && bsControl.ControlType == BsControlType.SortableList)
                    {
                        nestedValuesProperty = prop;
                        break;
                    }
                }
            }

            if (nestedValuesProperty != null)
            {
                var value = nestedValuesProperty.GetValue(model, null);

                return value != null && (value as IEnumerable<TModel>) != null ? value as IEnumerable<TModel> : null;
            }

            return null;
        }

        private bool IsValidConnection(TModel model, TModel candidateModel, Expression<Func<TModel, bool>> expression)
        {
            return expression == null || expression.Compile().Invoke(candidateModel);
        }

        #endregion

        public string RenderInternal(SortableListItemWrapper list)
        {
            var htmlString = String.Empty;

            if (list.BadgeTag.Attributes.ContainsKey("class"))
            {
                list.BadgeTag.Attributes["class"] += " label";
            }
            else
            {
                list.BadgeTag.Attributes.Add("class", "label");
            }

            if (list.ItemTag.Attributes.ContainsKey("class"))
            {
                list.ItemTag.Attributes["class"] += " bs-sortable-item";
            }
            else
            {
                list.ItemTag.Attributes.Add("class", "bs-sortable-item");
            }

            #region Nested elements

            if (list.RootTag.Attributes.ContainsKey("class"))
            {
                list.RootTag.Attributes["class"] += " bs-sortable";
            }
            else
            {
                list.RootTag.Attributes.Add("class", "bs-sortable");
            }

            if (list.Children != null && list.Children.Any())
            {
                foreach (var child in list.Children)
                {
                    list.RootTag.InnerHtml += RenderInternal(child);
                }
            }

            #endregion


            list.ItemTag.InnerHtml += list.BadgeTag.ToString() + list.LabelTag.ToString() + list.RootTag.ToString();

            htmlString = list.ItemTag.ToString();

            return htmlString;
        }



        public BsSortableListHtmlBuilder<TModel> Renderer(BsBaseRenderer<BsSortableListHtmlBuilder<TModel>> renderer)
        {
            renderer.Register(this);
            this.renderer = renderer;

            return this;
        }
        #endregion


        #region Fluent methods

        public BsSortableListHtmlBuilder<TModel> ItemProperties(Expression<Func<TModel, HtmlProperties>> itemProperties)
        {
            this.List = Build(this.Model, _config, _globalProperties, itemProperties, _labelProperties, _badgeProperties);

            _itemProperties = itemProperties;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> LabelProperties(Expression<Func<TModel, HtmlProperties>> labelProperties)
        {
            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, labelProperties, _badgeProperties);

            _labelProperties = labelProperties;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> BadgeProperties(Expression<Func<TModel, HtmlProperties>> badgeProperties)
        {
            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, badgeProperties);

            _badgeProperties = badgeProperties;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> ListProperties(Expression<Func<TModel, HtmlProperties>> listProperties)
        {
            this.List = Build(this.Model, _config, listProperties, _itemProperties, _labelProperties, _badgeProperties);

            _globalProperties = listProperties;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> Configure(Expression<Func<TModel, BsSortableListConfiguration<TModel>>> config)
        {
            this.List = Build(this.Model, config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            _config = config;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> ParentProperty(string propertyName)
        {
            this.ParentPropertyName = propertyName;

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> MigrationPermited(bool permited)
        {
            if (globalDataAttributes.ContainsKey("migration-permited"))
            {
                globalDataAttributes["migration-permited"] = permited;
            }
            else
            {
                globalDataAttributes.Add("migration-permited", permited);
            }

            return this;
        }

        #region Individual component configuration

        public BsSortableListHtmlBuilder<TModel> HtmlAttributes(object attributes)
        {
            var globalPropertiesFunc = _globalProperties.Compile();

            _globalProperties = x => new HtmlProperties
            {
                HtmlAttributes = attributes,
                DataAttributes = globalPropertiesFunc.Invoke(x).DataAttributes,
                Text = globalPropertiesFunc.Invoke(x).Text
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> DataAttributes(object attributes)
        {
            var globalPropertiesFunc = _globalProperties.Compile();

            _globalProperties = x => new HtmlProperties
            {
                HtmlAttributes = globalPropertiesFunc.Invoke(x).HtmlAttributes,
                DataAttributes = attributes,
                Text = globalPropertiesFunc.Invoke(x).Text
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> ItemHtmlAttributes(Expression<Func<TModel, object>> attributes)
        {
            var itemPropertiesFunc = _itemProperties.Compile();
            var attributesFunc = attributes.Compile();

            _itemProperties = x => new HtmlProperties
            {
                HtmlAttributes = attributesFunc.Invoke(x),
                DataAttributes = itemPropertiesFunc.Invoke(x).DataAttributes,
                Text = itemPropertiesFunc.Invoke(x).Text
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> BadgeHtmlAttributes(Expression<Func<TModel, object>> attributes)
        {
            var badgePropertiesFunc = _badgeProperties.Compile();
            var attributesFunc = attributes.Compile();

            _badgeProperties = x => new HtmlProperties
            {
                HtmlAttributes = attributesFunc.Invoke(x),
                DataAttributes = badgePropertiesFunc.Invoke(x).DataAttributes,
                Text = badgePropertiesFunc.Invoke(x).Text
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> LabelHtmlAttributes(Expression<Func<TModel, object>> attributes)
        {
            var labelPropertiesFunc = _labelProperties.Compile();
            var attributesFunc = attributes.Compile();

            _labelProperties = x => new HtmlProperties
            {
                HtmlAttributes = attributesFunc.Invoke(x),
                DataAttributes = labelPropertiesFunc.Invoke(x).DataAttributes,
                Text = labelPropertiesFunc.Invoke(x).Text
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> BadgeText(Expression<Func<TModel, string>> text)
        {
            var badgePropertiesFunc = _badgeProperties.Compile();
            var textFunc = text.Compile();

            _badgeProperties = x => new HtmlProperties
            {
                HtmlAttributes = badgePropertiesFunc.Invoke(x).HtmlAttributes,
                DataAttributes = badgePropertiesFunc.Invoke(x).DataAttributes,
                Text = textFunc.Invoke(x)
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        public BsSortableListHtmlBuilder<TModel> LabelText(Expression<Func<TModel, string>> text)
        {
            var labelPropertiesFunc = _labelProperties.Compile();
            var textFunc = text.Compile();

            _labelProperties = x => new HtmlProperties
            {
                HtmlAttributes = labelPropertiesFunc.Invoke(x).HtmlAttributes,
                DataAttributes = labelPropertiesFunc.Invoke(x).DataAttributes,
                Text = textFunc.Invoke(x)
            };

            this.List = Build(this.Model, _config, _globalProperties, _itemProperties, _labelProperties, _badgeProperties);

            return this;
        }

        #endregion

        #endregion

        #region Helpers

        private RouteValueDictionary DictionaryFromAttributes(object attributes, string keyPrefix = "")
        {
            var returnValue = new RouteValueDictionary();

            if (attributes != null)
            {
                returnValue = attributes is IDictionary<string, object>
                    ? attributes as RouteValueDictionary
                    : HtmlHelper.AnonymousObjectToHtmlAttributes(attributes);
            }

            returnValue = returnValue ?? new RouteValueDictionary();

            if (!String.IsNullOrEmpty(keyPrefix))
            {
                returnValue = new RouteValueDictionary(returnValue.ToDictionary(x => keyPrefix + x.Key, y => y.Value));
            }

            return returnValue;
        }

        #endregion

    }

    public class BsSortableBaseRenderer<TModel> : BsBaseRenderer<BsSortableListHtmlBuilder<TModel>>
    {
        public override string Render()
        {
            var innerHtml = this.Builder.List.Children.Aggregate(String.Empty, (current, item) => current + this.RenderInternal(item));

            var ol = new TagBuilder("ol") { InnerHtml = innerHtml };

            ol.MergeAttributes(this.Builder.globalHtmlAttributes);
            ol.MergeAttributes(this.Builder.globalDataAttributes.ToDictionary(x => "data-" + x.Key, y => y.Value));

            if (this.Builder.globalHtmlAttributes.ContainsKey("class"))
            {
                ol.Attributes["class"] += " bs-sortable";
            }
            else
            {
                ol.Attributes.Add("class", "bs-sortable");
            }

            var container = new TagBuilder("div") { InnerHtml = ol.ToString() };

            container.Attributes.Add("class", "sortable-container");
            container.Attributes.Add("data-parent-property", this.Builder.ParentPropertyName);
            container.Attributes.Add("data-renderer", "base");

            return container.ToString();
        }

        protected string RenderInternal(SortableListItemWrapper list)
        {
            var htmlString = String.Empty;

            if (list.BadgeTag.Attributes.ContainsKey("class"))
            {
                list.BadgeTag.Attributes["class"] += " label";
            }
            else
            {
                list.BadgeTag.Attributes.Add("class", "label");
            }

            if (list.ItemTag.Attributes.ContainsKey("class"))
            {
                list.ItemTag.Attributes["class"] += " bs-sortable-item";
            }
            else
            {
                list.ItemTag.Attributes.Add("class", "bs-sortable-item");
            }

            #region Nested elements

            if (list.RootTag.Attributes.ContainsKey("class"))
            {
                list.RootTag.Attributes["class"] += " bs-sortable";
            }
            else
            {
                list.RootTag.Attributes.Add("class", "bs-sortable");
            }

            if (list.Children != null && list.Children.Any())
            {
                foreach (var child in list.Children)
                {
                    list.RootTag.InnerHtml += RenderInternal(child);
                }
            }

            #endregion


            list.ItemTag.InnerHtml += list.BadgeTag.ToString() + list.LabelTag.ToString() + list.RootTag.ToString();

            htmlString = list.ItemTag.ToString();

            return htmlString;
        }
    }

    public class BsSortableLightRenderer<TModel> : BsBaseRenderer<BsSortableListHtmlBuilder<TModel>>
    {
        public override string Render()
        {
            var innerHtml = this.Builder.List.Children.Aggregate(String.Empty, (current, item) => current + this.RenderInternal(item));

            var ul = new TagBuilder("ul") { InnerHtml = innerHtml };

            ul.MergeAttributes(this.Builder.globalHtmlAttributes);
            ul.MergeAttributes(this.Builder.globalDataAttributes.ToDictionary(x => "data-" + x.Key, y => y.Value));

            if (this.Builder.globalHtmlAttributes.ContainsKey("class"))
            {
                ul.Attributes["class"] += " bs-sortable nav nav-pills nav-stacked";
            }
            else
            {
                ul.Attributes.Add("class", "bs-sortable nav nav-pills nav-stacked");
            }

            ul.Attributes.Add("style", "margin-top: 2px");

            var container = new TagBuilder("div") { InnerHtml = ul.ToString() };

            container.Attributes.Add("class", "sortable-container");
            container.Attributes.Add("data-parent-property", this.Builder.ParentPropertyName);
            container.Attributes.Add("data-renderer", "light");

            return container.ToString();
        }

        public string RenderInternal(SortableListItemWrapper list)
        {
            var htmlString = String.Empty;

            list.RootTag = new TagBuilder("ul");

            if (list.ItemTag.Attributes.ContainsKey("class"))
            {
                list.ItemTag.Attributes["class"] += " bs-sortable-item active";
            }
            else
            {
                list.ItemTag.Attributes.Add("class", "bs-sortable-item active");
            }

            #region Nested elements

            if (list.RootTag.Attributes.ContainsKey("class"))
            {
                list.RootTag.Attributes["class"] += " bs-sortable nav nav-pills nav-stacked";
            }
            else
            {
                list.RootTag.Attributes.Add("class", "bs-sortable nav nav-pills nav-stacked");
            }

            if (list.RootTag.Attributes.ContainsKey("style"))
            {
                list.RootTag.Attributes["style"] += "margin-left: 40px; margin-top: 4px;";
            }
            else
            {
                list.RootTag.Attributes.Add("style", "margin-left: 40px; margin-top: 4px;");
            }

            if (list.Children != null && list.Children.Any())
            {
                foreach (var child in list.Children)
                {
                    list.RootTag.InnerHtml += RenderInternal(child);
                }
            }

            #endregion

            var anchor = new TagBuilder("a");

            anchor.Attributes.Add("href", "#");
            anchor.SetInnerText(list.LabelTag.InnerHtml);

            var span = new TagBuilder("span");

            span.Attributes.Add("class", "badge pull-right");
            span.InnerHtml = list.BadgeTag.InnerHtml;

            anchor.InnerHtml = span.ToString() + anchor.InnerHtml;

            list.ItemTag.InnerHtml = anchor.ToString() + list.RootTag.ToString();

            // list.ItemTag.InnerHtml += spanTag.ToString() + list.RootTag.ToString();

            // htmlString = list.ItemTag.ToString() + list.RootTag.ToString();

            htmlString = list.ItemTag.ToString();

            return htmlString;
        }
    }

}