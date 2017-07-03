using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using LanguageExt;
using Newtonsoft.Json.Linq;

namespace ExRam.Gremlinq
{
    public static class JsonExtensions
    {
        private sealed class EnumerableJsonReader : JsonReader
        {
            private readonly IEnumerator<(JsonToken tokenType, object tokenValue)> _enumerator;

            public EnumerableJsonReader(IEnumerator<(JsonToken tokenType, object tokenValue)> enumerator)
            {
                this._enumerator = enumerator;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    this._enumerator.Dispose();

                base.Dispose(disposing);
            }

            public override bool Read()
            {
                return this._enumerator.MoveNext();
            }

            public override JsonToken TokenType => this._enumerator.Current.tokenType;

            public override object Value => this._enumerator.Current.tokenValue;
        }

        public static IEnumerable<(JsonToken tokenType, object tokenValue)> ToTokenEnumerable(this JsonReader jsonReader)
        {
            while (jsonReader.Read())
                yield return (jsonReader.TokenType, jsonReader.Value);
        }

        public static JsonReader ToJsonReader(this IEnumerable<(JsonToken tokenType, object tokenValue)> enumerable)
        {
            return new EnumerableJsonReader(enumerable.GetEnumerator());
        }

        public static IEnumerable<(JsonToken tokenType, object tokenValue)> Apply(this IEnumerable<(JsonToken tokenType, object tokenValue)> source, Func<IEnumerator<(JsonToken tokenType, object tokenValue)>, IEnumerator<(JsonToken tokenType, object tokenValue)>> transformation)
        {
            using (var e = transformation(source.GetEnumerator()))
            {
                while (e.MoveNext())
                    yield return e.Current;
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> HidePropertyName(this IEnumerator<(JsonToken tokenType, object tokenValue)> enumerator, string property, Func<IEnumerator<(JsonToken tokenType, object tokenValue)>, IEnumerator<(JsonToken tokenType, object tokenValue)>> innerTransformation)
        {
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;

                if (current.tokenType == JsonToken.PropertyName)
                {
                    if (property.Equals(current.tokenValue as string, StringComparison.Ordinal))
                    {
                        using (var inner = innerTransformation(enumerator))
                        {
                            while (inner.MoveNext())
                            {
                                yield return inner.Current;
                            }
                        }

                        continue;
                    }
                }
                
                yield return current;
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> ReadValue(this IEnumerator<(JsonToken tokenType, object tokenValue)> source)
        {
            var openArrays = 0;
            var openObjects = 0;

            while (source.MoveNext())
            {
                var current = source.Current;

                switch (current.tokenType)
                {
                    case JsonToken.StartObject:
                        openObjects++;
                        break;
                    case JsonToken.StartArray:
                        openArrays++;
                        break;
                    case JsonToken.EndObject:
                        openObjects--;
                        break;
                    case JsonToken.EndArray:
                        openArrays--;
                        break;
                }

                yield return current;

                if (openArrays == 0 && openObjects == 0)
                    break;
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> Unwrap(this IEnumerator<(JsonToken tokenType, object tokenValue)> source)
        {
            if (source.MoveNext())
            {
                if (source.Current.tokenType == JsonToken.StartObject)
                {
                    var openObjects = 0;

                    while (source.MoveNext())
                    {
                        if (source.Current.tokenType == JsonToken.StartObject)
                            openObjects++;
                        else if (source.Current.tokenType == JsonToken.EndObject)
                        {
                            if (openObjects == 0)
                                yield break;

                            openObjects--;
                        }

                        yield return source.Current;
                    }
                }
                else
                {
                    yield return source.Current;

                    while (source.MoveNext())
                        yield return source.Current;
                }
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> TakeOne(this IEnumerator<(JsonToken tokenType, object tokenValue)> source, Func<IEnumerator<(JsonToken tokenType, object tokenValue)>, IEnumerator<(JsonToken tokenType, object tokenValue)>> innerTransformation)
        {
            while (source.MoveNext())
            {
                if (source.Current.tokenType == JsonToken.StartArray)
                {
                    var openArrays = 1;

                    using (var e = innerTransformation(source.ReadValue()))
                    {
                        while (e.MoveNext())
                            yield return e.Current;
                    }

                    while (source.MoveNext())
                    {
                        if (source.Current.tokenType == JsonToken.StartArray)
                            openArrays++;
                        else if (source.Current.tokenType == JsonToken.EndArray)
                            openArrays--;

                        if (openArrays == 0)
                            yield break;
                    }
                }
                else
                {
                    yield return source.Current;
                }
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> ExtractProperty(this IEnumerator<(JsonToken tokenType, object tokenValue)> source, string property)
        {
            while (source.MoveNext())
            {
                if (source.Current.tokenType == JsonToken.PropertyName && property.Equals(source.Current.tokenValue as string))
                {
                    using (var e = source.ReadValue())
                    {
                        while (e.MoveNext())
                            yield return e.Current;
                    }

                    break;
                }
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> SelectPropertyNode(this IEnumerator<(JsonToken tokenType, object tokenValue)> source, Func<IEnumerator<(JsonToken tokenType, object tokenValue)>, IEnumerator<(JsonToken tokenType, object tokenValue)>> projection)
        {
            while (source.MoveNext())
            {
                if (source.Current.tokenType == JsonToken.PropertyName)
                {
                    yield return source.Current;

                    using (var e = projection(source.ReadValue()))
                    {
                        while (e.MoveNext())
                            yield return e.Current;
                    }
                }
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> SelectPropertyNode(this IEnumerator<(JsonToken tokenType, object tokenValue)> source, string propertyName, Func<IEnumerator<(JsonToken tokenType, object tokenValue)>, IEnumerator<(JsonToken tokenType, object tokenValue)>> projection)
        {
            while (source.MoveNext())
            {
                if (source.Current.tokenType == JsonToken.PropertyName && propertyName.Equals(source.Current.tokenValue as string))
                {
                    yield return source.Current;

                    using (var e = projection(source.ReadValue()))
                    {
                        while (e.MoveNext())
                            yield return e.Current;
                    }

                    continue;
                }

                yield return source.Current;
            }
        }

        public static IEnumerator<(JsonToken tokenType, object tokenValue)> SelectToken(this IEnumerator<(JsonToken tokenType, object tokenValue)> source, Func<(JsonToken tokenType, object tokenValue), (JsonToken tokenType, object tokenValue)> projection)
        {
            while (source.MoveNext())
            {
                yield return projection(source.Current);
            }
        }
    }

    public static class GremlinQueryProvider
    {
        private abstract class GremlinQueryProviderBase : IGremlinQueryProvider
        {
            private readonly IGremlinQueryProvider _baseGremlinQueryProvider;

            protected GremlinQueryProviderBase(IGremlinQueryProvider baseGremlinQueryProvider)
            {
                this._baseGremlinQueryProvider = baseGremlinQueryProvider;
            }

            // ReSharper disable once MemberHidesStaticFromOuterClass
            public virtual IAsyncEnumerable<T> Execute<T>(IGremlinQuery<T> query)
            {
                return this._baseGremlinQueryProvider.Execute(query);
            }
        }

        private sealed class JsonSupportGremlinQueryProvider : IGremlinQueryProvider
        {
            private readonly IModelGremlinQueryProvider _baseProvider;

            private sealed class JsonGremlinDeserializer : IGremlinDeserializer
            {
                private readonly IGremlinQuery _query;

                private sealed class StepLabelMappingsContractResolver : DefaultContractResolver
                {
                    private readonly IImmutableDictionary<string, StepLabel> _mappings;

                    public StepLabelMappingsContractResolver(IImmutableDictionary<string, StepLabel> mappings)
                    {
                        this._mappings = mappings;
                    }

                    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
                    {
                        var property = base.CreateProperty(member, memberSerialization);

                        this._mappings
                            .TryGetValue(member.Name)
                            .IfSome(
                                mapping =>
                                {
                                    property.PropertyName = mapping.Label;
                                });

                        return property;
                    }
                }

                private sealed class TimespanConverter : JsonConverter
                {
                    public override bool CanConvert(Type objectType)
                    {
                        return objectType == typeof(TimeSpan);
                    }

                    public override bool CanRead => true;
                    public override bool CanWrite => true;

                    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
                    {
                        if (objectType != typeof(TimeSpan))
                            throw new ArgumentException();

                        var spanString = reader.Value as string;
                        if (spanString == null)
                            return null;
                        return XmlConvert.ToTimeSpan(spanString);
                    }

                    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
                    {
                        var duration = (TimeSpan)value;
                        writer.WriteValue(XmlConvert.ToString(duration));
                    }
                }
                
                public JsonGremlinDeserializer(IGremlinQuery query)
                {
                    this._query = query;
                }
                
                public IAsyncEnumerable<T> Deserialize<T>(string rawData, IGraphModel model)
                {
                    var serializer = new JsonSerializer
                    {
                        Converters = { new TimespanConverter() },
                        ContractResolver = new StepLabelMappingsContractResolver(this._query.StepLabelMappings),
                        TypeNameHandling = TypeNameHandling.Auto,
                        MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead
                    };
                    
                    return AsyncEnumerable.Return(rawData.StartsWith("{") || rawData.StartsWith("[")
                        ? serializer.Deserialize<T>(new JsonTextReader(new StringReader(rawData))
                            .ToTokenEnumerable()
                            .Apply(e => e
                                .HidePropertyName(
                                    "id",
                                    idSection => idSection
                                        .Unwrap())
                                .HidePropertyName(
                                    "properties",
                                    propertiesSection => propertiesSection
                                        .Unwrap()
                                        .SelectPropertyNode(prop => prop
                                            .TakeOne(y => y
                                                .Unwrap()
                                                .ExtractProperty("value"))))
                                .SelectToken(tuple => tuple.tokenType == JsonToken.PropertyName && "label".Equals(tuple.tokenValue)
                                    ? (JsonToken.PropertyName, "$type")
                                    : tuple)
                                .SelectPropertyNode("$type", typeNode => typeNode
                                    .SelectToken(tuple =>
                                    {
                                        if (tuple.tokenType == JsonToken.String)
                                        {
                                            return model
                                                .TryGetElementTypeOfLabel(tuple.tokenValue as string)
                                                .Map(suitableType => (JsonToken.String, (object)suitableType.AssemblyQualifiedName))
                                                .IfNone(tuple);
                                        }

                                        return tuple;
                                    })))
                            .ToJsonReader())
                        : JToken.Parse($"'{rawData}'").ToObject<T>());
                }
            }

            public JsonSupportGremlinQueryProvider(IModelGremlinQueryProvider baseProvider)
            {
                this._baseProvider = baseProvider;
            }

            // ReSharper disable once MemberHidesStaticFromOuterClass
            public IAsyncEnumerable<T> Execute<T>(IGremlinQuery<T> query)
            {
                return this._baseProvider
                    .Execute(query)
                    .SelectMany(rawData => new JsonGremlinDeserializer(query)
                        .Deserialize<T>(rawData, this._baseProvider.Model));
            }
        }

        private sealed class ModelGremlinQueryProvider : IModelGremlinQueryProvider
        {
            private readonly INativeGremlinQueryProvider _baseProvider;

            public ModelGremlinQueryProvider(INativeGremlinQueryProvider baseProvider, IGraphModel newModel)
            {
                this.Model = newModel;
                this._baseProvider = baseProvider;
            }

            public IAsyncEnumerable<string> Execute(IGremlinQuery query)
            {
                var serialized = query.Serialize(this.Model, false);

                return this._baseProvider
                    .Execute(serialized.queryString, serialized.parameters);
            }

            public IGraphModel Model { get; }
        }

        private sealed class SubgraphStrategyQueryProvider : GremlinQueryProviderBase
        {
            private readonly Func<IGremlinQuery<Unit>, IGremlinQuery> _edgeCriterion;
            private readonly Func<IGremlinQuery<Unit>, IGremlinQuery> _vertexCriterion;

            public SubgraphStrategyQueryProvider(IGremlinQueryProvider baseGremlinQueryProvider, Func<IGremlinQuery<Unit>, IGremlinQuery> vertexCriterion, Func<IGremlinQuery<Unit>, IGremlinQuery> edgeCriterion) : base(baseGremlinQueryProvider)
            {
                this._edgeCriterion = edgeCriterion;
                this._vertexCriterion = vertexCriterion;
            }

            public override IAsyncEnumerable<T> Execute<T>(IGremlinQuery<T> query)
            {
                var castedQuery = query
                    .Cast<Unit>();

                var vertexCriterionTraversal = this._vertexCriterion(castedQuery.ToAnonymous());
                var edgeCriterionTraversal = this._edgeCriterion(castedQuery.ToAnonymous());

                var strategy = GremlinQuery
                    .Create("SubgraphStrategy")
                    .AddStep<Unit>("build");

                if (vertexCriterionTraversal.Steps.Count > 0)
                    strategy = strategy.AddStep<Unit>("vertices", vertexCriterionTraversal);

                if (edgeCriterionTraversal.Steps.Count > 0)
                    strategy = strategy.AddStep<Unit>("edges", edgeCriterionTraversal);

                query = query
                    .InsertStep<T>(0, new TerminalGremlinStep("withStrategies", strategy.AddStep<Unit>("create")));

                return base.Execute(query);
            }
        }

        private sealed class RewriteStepsQueryProvider<TStep> : GremlinQueryProviderBase where TStep : GremlinStep
        {
            private readonly Func<TStep, GremlinStep> _replacementStepFactory;

            public RewriteStepsQueryProvider(IGremlinQueryProvider baseGremlinQueryProvider, Func<TStep, GremlinStep> replacementStepFactory) : base(baseGremlinQueryProvider)
            {
                this._replacementStepFactory = replacementStepFactory;
            }

            public override IAsyncEnumerable<T> Execute<T>(IGremlinQuery<T> query)
            {
                return base.Execute(RewriteSteps(query).Cast<T>());
            }
                    
            private IGremlinQuery RewriteSteps(IGremlinQuery query)
            {
                var steps = query.Steps;

                for (var i = 0; i < steps.Count; i++)
                {
                    var step = query.Steps[i];
                    
                    if (step is TerminalGremlinStep terminal)
                    {
                        var parameters = terminal.Parameters;

                        for (var j = 0; j < parameters.Count; j++)
                        {
                            var parameter = parameters[j];

                            if (parameter is IGremlinQuery subQuery)
                                parameters = parameters.SetItem(j, RewriteSteps(subQuery));
                        }

                        // ReSharper disable once PossibleUnintendedReferenceComparison
                        if (parameters != terminal.Parameters)
                            step = new TerminalGremlinStep(terminal.Name, parameters);
                    }
                    else if (step is TStep replacedStep)
                    {
                        step = this._replacementStepFactory(replacedStep);
                    }

                    if (step != query.Steps[i])
                        steps = steps.SetItem(i, step);
                }

                // ReSharper disable once PossibleUnintendedReferenceComparison
                return steps != query.Steps
                    ? query.ReplaceSteps(steps) 
                    : query;
            }
        }

        public static IAsyncEnumerable<T> Execute<T>(this IGremlinQuery<T> query, IGremlinQueryProvider provider)
        {
            return provider.Execute(query);
        }

        public static IGremlinQueryProvider WithJsonSupport(this IModelGremlinQueryProvider provider)
        {
            return new JsonSupportGremlinQueryProvider(provider);
        }

        public static IModelGremlinQueryProvider WithModel(this INativeGremlinQueryProvider provider, IGraphModel model)
        {
            return new ModelGremlinQueryProvider(provider, model);
        }

        public static IGremlinQueryProvider WithSubgraphStrategy(this IGremlinQueryProvider provider, Func<IGremlinQuery<Unit>, IGremlinQuery> vertexCriterion, Func<IGremlinQuery<Unit>, IGremlinQuery> edgeCriterion)
        {
            return new SubgraphStrategyQueryProvider(provider, vertexCriterion, edgeCriterion);
        }
        
        public static IGremlinQueryProvider OverrideElementProperty<TSource, TProperty>(this IGremlinQueryProvider provider, Func<TSource, bool> overrideCriterion, Expression<Func<TSource, TProperty>> memberExpression, TProperty value)
        {
            return provider
                .RewriteSteps<AddElementPropertiesStep>(step =>
                {
                    if (step.Element is TSource source)
                    {
                        if (overrideCriterion(source))
                            return new ReplaceElementPropertyStep<TSource, TProperty>(step, memberExpression, value);
                    }

                    return step;
                });
        }

        public static IGremlinQueryProvider RewriteSteps<TStep>(this IGremlinQueryProvider provider, Func<TStep, GremlinStep> replacementStepFactory) where TStep : GremlinStep
        {
            return new RewriteStepsQueryProvider<TStep>(provider, replacementStepFactory);
        }
    }
}