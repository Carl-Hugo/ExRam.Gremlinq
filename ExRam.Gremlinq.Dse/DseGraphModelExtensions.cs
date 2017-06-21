using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using LanguageExt;

namespace ExRam.Gremlinq.Dse
{
    public static class DseGraphModelExtensions
    {
        private sealed class DseGraphModel : IDseGraphModel
        {
            public DseGraphModel(
                IImmutableDictionary<Type, string> vertexLabels, 
                IImmutableDictionary<Type, string> edgeTypes,
                IImmutableDictionary<Type, IImmutableSet<(Type, Type)>> connections, 
                IImmutableDictionary<Type, Expression> primaryKeys,
                IImmutableDictionary<Type, IImmutableSet<Expression>> materializedIndexes,
                IImmutableDictionary<Type, IImmutableSet<Expression>> secondaryIndexes,
                IImmutableDictionary<Type, Expression> searchIndexes,
                IImmutableDictionary<Type, IImmutableSet<(Type vertexType, Expression indexExpression, EdgeDirection direction)>> edgeIndexes)
            {
                this.VertexLabels = vertexLabels;
                this.EdgeLabels = edgeTypes;
                this.Connections = connections;
                this.PrimaryKeys = primaryKeys;
                this.MaterializedIndexes = materializedIndexes;
                this.SecondaryIndexes = secondaryIndexes;
                this.SearchIndexes = searchIndexes;
                this.EdgeIndexes = edgeIndexes;
            }

            public IImmutableDictionary<Type, string> VertexLabels { get; }

            public IImmutableDictionary<Type, string> EdgeLabels { get; }

            public IImmutableDictionary<Type, IImmutableSet<(Type, Type)>> Connections { get; }

            public IImmutableDictionary<Type, Expression> PrimaryKeys { get; }

            public IImmutableDictionary<Type, IImmutableSet<Expression>> MaterializedIndexes { get; }

            public IImmutableDictionary<Type, IImmutableSet<Expression>> SecondaryIndexes { get; }

            public IImmutableDictionary<Type, Expression> SearchIndexes { get; }

            public IImmutableDictionary<Type, IImmutableSet<(Type vertexType, Expression indexExpression, EdgeDirection direction)>> EdgeIndexes { get; }
        }

        private static readonly IReadOnlyDictionary<Type, string> NativeTypeSteps = new Dictionary<Type, string>
        {
            { typeof(long), "Bigint" },
            { typeof(byte[]), "Blob" },
            { typeof(bool), "Boolean" },
            { typeof(decimal), "Decimal" },
            { typeof(double), "Double" },
            { typeof(TimeSpan), "Duration" },
            { typeof(IPAddress), "Inet" },
            { typeof(int), "Int" },
            //{ typeof(?), new GremlinStep("Linestring") },
            //{ typeof(?), new GremlinStep("Point") },
            //{ typeof(?), new GremlinStep("Polygon") },
            { typeof(short), "Smallint" },
            { typeof(string), "Text" },
            { typeof(DateTime), "Timestamp" },
            { typeof(Guid), "Uuid" }
            //{ typeof(?), new GremlinStep("Varint") },
        };

        public static IDseGraphModel ToDseGraphModel(this IGraphModel model)
        {
            return new DseGraphModel(
                model.VertexLabels, 
                model.EdgeLabels, 
                ImmutableDictionary<Type, IImmutableSet<(Type, Type)>>.Empty,
                ImmutableDictionary<Type, Expression>.Empty, 
                ImmutableDictionary<Type, IImmutableSet<Expression>>.Empty, 
                ImmutableDictionary<Type, IImmutableSet<Expression>>.Empty,
                ImmutableDictionary<Type, Expression>.Empty,
                ImmutableDictionary<Type, IImmutableSet<(Type vertexType, Expression indexExpression, EdgeDirection direction)>>.Empty);
        }

        public static IDseGraphModel EdgeConnectionClosure(this IDseGraphModel model)
        {
            foreach (var kvp in model.Connections)
            {
                foreach (var edgeClosure in model.GetDerivedElementInfos(kvp.Key, true))
                {
                    foreach (var tuple in kvp.Value)
                    {
                        foreach (var outVertexClosure in model.GetDerivedElementInfos(tuple.Item1, true))
                        {
                            foreach (var inVertexClosure in model.GetDerivedElementInfos(tuple.Item2, true))
                            {
                                model = model.AddConnection(outVertexClosure, edgeClosure, inVertexClosure);
                            }
                        }
                    }
                }
            }

            return model;
        }

        public static IDseGraphModel AddConnection<TOutVertex, TEdge, TInVertex>(this IDseGraphModel model)
        {
            return model.AddConnection(typeof(TOutVertex), typeof(TEdge), typeof(TInVertex));
        }

        public static IDseGraphModel PrimaryKey<T>(this IDseGraphModel model, Expression<Func<T, object>> expression)
        {
            var newPrimaryKeys = model.PrimaryKeys.SetItem(typeof(T), expression);

            return newPrimaryKeys != model.PrimaryKeys
                ? new DseGraphModel(
                    model.VertexLabels, 
                    model.EdgeLabels, 
                    model.Connections,
                    newPrimaryKeys,
                    model.MaterializedIndexes, 
                    model.SecondaryIndexes,
                    model.SearchIndexes,
                    model.EdgeIndexes)
                : model;
        }

        public static IDseGraphModel MaterializedIndex<T>(this IDseGraphModel model, Expression<Func<T, object>> indexExpression)
        {
            var newMaterializedIndexes = model.MaterializedIndexes.Add(typeof(T), indexExpression);

            return newMaterializedIndexes != model.MaterializedIndexes
                ? new DseGraphModel(
                    model.VertexLabels,
                    model.EdgeLabels,
                    model.Connections,
                    model.PrimaryKeys,
                    model.MaterializedIndexes.Add(typeof(T), indexExpression),
                    model.SecondaryIndexes,
                    model.SearchIndexes,
                    model.EdgeIndexes)
                : model;
        }

        public static IDseGraphModel SecondaryIndex<T>(this IDseGraphModel model, Expression<Func<T, object>> indexExpression)
        {
            var newSecondaryIndexes = model.SecondaryIndexes.Add(typeof(T), indexExpression);

            return newSecondaryIndexes != model.SecondaryIndexes 
                ? new DseGraphModel(
                    model.VertexLabels,
                    model.EdgeLabels,
                    model.Connections,
                    model.PrimaryKeys,
                    model.MaterializedIndexes,
                    newSecondaryIndexes,
                    model.SearchIndexes,
                    model.EdgeIndexes)
                :  model;
        }

        public static IDseGraphModel SearchIndex<T>(this IDseGraphModel model, Expression<Func<T, object>> indexExpression)
        {
            var newSearchIndexes = model.SearchIndexes.SetItem(typeof(T), indexExpression);

            return newSearchIndexes != model.SearchIndexes
                ? new DseGraphModel(
                    model.VertexLabels,
                    model.EdgeLabels,
                    model.Connections,
                    model.PrimaryKeys,
                    model.MaterializedIndexes,
                    model.SecondaryIndexes,
                    newSearchIndexes,
                    model.EdgeIndexes)
                : model;
        }

        public static IDseGraphModel EdgeIndex<TVertex, TEdge>(this IDseGraphModel model, Expression<Func<TEdge, object>> indexExpression, EdgeDirection direction)
        {
            var newEdgeIndexes = model.EdgeIndexes.Add(typeof(TEdge), (typeof(TVertex), indexExpression, direction));

            return newEdgeIndexes != model.EdgeIndexes
                ? new DseGraphModel(
                    model.VertexLabels,
                    model.EdgeLabels,
                    model.Connections,
                    model.PrimaryKeys,
                    model.MaterializedIndexes,
                    model.SecondaryIndexes,
                    model.SearchIndexes,
                    newEdgeIndexes)
                : model;
        }

        public static IEnumerable<IGremlinQuery<string>> CreateSchemaQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            model = model
                .EdgeConnectionClosure();

            return model
                .CreatePropertyKeyQueries(queryProvider)
                .Concat(model.CreateVertexLabelQueries(queryProvider))
                .Concat(model.CreateVertexMaterializedIndexQueries(queryProvider))
                .Concat(model.CreateVertexSecondaryIndexQueries(queryProvider))
                .Concat(model.CreateVertexSearchIndexQueries(queryProvider))
                .Concat(model.CreateEdgeLabelQueries(queryProvider))
                .Concat(model.CreateEdgeIndexQueries(queryProvider));
        }

        private static IEnumerable<IGremlinQuery<string>> CreatePropertyKeyQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            var propertyKeys = new Dictionary<string, Type>();

            foreach (var type in model.VertexLabels.Keys.Concat(model.EdgeLabels.Keys))
            {
                foreach (var property in type.GetProperties())
                {
                    var propertyType = property.PropertyType;

                    while (true)
                    {
                        if (propertyType.GetTypeInfo().IsEnum)
                            propertyType = Enum.GetUnderlyingType(propertyType);
                        else
                        {
                            var maybeNullableType = Nullable.GetUnderlyingType(propertyType);
                            if (maybeNullableType != null)
                                propertyType = maybeNullableType;
                            else
                                break;
                        }
                    }

                    if (propertyKeys.TryGetValue(property.Name, out var existingType))
                    {
                        if (existingType != propertyType) //TODO: Support any kind of inheritance here?
                            throw new InvalidOperationException($"Property {property.Name} already exists with type {existingType.Name}.");
                    }
                    else
                        propertyKeys.Add(property.Name, propertyType);
                }
            }

            return propertyKeys
                .Select(propertyInfoKvp => GremlinQuery
                    .Create("schema", queryProvider)
                    .AddStep<string>("propertyKey", propertyInfoKvp.Key)
                    .AddStep<string>(NativeTypeSteps
                        .TryGetValue(propertyInfoKvp.Value)
                        .IfNone(() => throw new InvalidOperationException($"No native type found for {propertyInfoKvp.Value}.")))
                    .AddStep<string>("single")
                    .AddStep<string>("ifNotExists")
                    .AddStep<string>("create"));
        }

        private static IEnumerable<IGremlinQuery<string>> CreateVertexSecondaryIndexQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.CreateIndexQueries(model.SecondaryIndexes, "secondary", queryProvider);
        }

        private static IEnumerable<IGremlinQuery<string>> CreateVertexMaterializedIndexQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.CreateIndexQueries(model.MaterializedIndexes, "materialized", queryProvider);
        }

        private static IEnumerable<IGremlinQuery<string>> CreateVertexSearchIndexQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.VertexLabels
                .Where(vertexKvp => !vertexKvp.Key.GetTypeInfo().IsAbstract)
                .Select(vertexKvp => (
                    Label: vertexKvp.Value,
                    IndexProperties: vertexKvp.Key
                        .GetTypeHierarchy(model)
                        .SelectMany(x => model.SearchIndexes
                            .TryGetValue(x)
                            .AsEnumerable())
                        .Take(1)
                        .Select(indexExpression => ((indexExpression as LambdaExpression)?.Body.StripConvert() as MemberExpression)?.Member.Name)
                        .ToImmutableList()))
                .Where(tuple => !tuple.IndexProperties.IsEmpty)
                .Select(tuple => tuple.IndexProperties
                    .Aggregate(
                        GremlinQuery
                            .Create("schema", queryProvider)
                            .AddStep<string>("vertexLabel", tuple.Label)
                            .AddStep<string>("index", "search")
                            .AddStep<string>("search"),
                        (closureQuery, indexProperty) => closureQuery.AddStep<string>("by", indexProperty))
                    .AddStep<string>("add"));
        }

        private static IEnumerable<IGremlinQuery<string>> CreateIndexQueries(this IDseGraphModel model, IImmutableDictionary<Type, IImmutableSet<Expression>> indexDictionary, string keyword, IGremlinQueryProvider queryProvider)
        {
            return model.VertexLabels
                .Where(vertexKvp => !vertexKvp.Key.GetTypeInfo().IsAbstract)
                .Select(vertexKvp => (
                    Label: vertexKvp.Value,
                    IndexProperties: vertexKvp.Key.GetTypeHierarchy(model)
                        .SelectMany(x => indexDictionary
                            .TryGetValue(x)
                            .AsEnumerable()
                            .SelectMany(y => y))
                        .Select(indexExpression => ((indexExpression as LambdaExpression)?.Body.StripConvert() as MemberExpression)?.Member.Name)
                        .ToImmutableList()))
                .Where(tuple => !tuple.IndexProperties.IsEmpty)
                .Select(tuple => tuple.IndexProperties
                    .Aggregate(
                        GremlinQuery
                            .Create("schema", queryProvider)
                            .AddStep<string>("vertexLabel", tuple.Label)
                            .AddStep<string>("index", Guid.NewGuid().ToString("N"))
                            .AddStep<string>(keyword),
                        (closureQuery, indexProperty) => closureQuery.AddStep<string>("by", indexProperty))
                    .AddStep<string>("add"));
        }

        private static IEnumerable<IGremlinQuery<string>> CreateVertexLabelQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.VertexLabels
                .Where(vertexKvp => !vertexKvp.Key.GetTypeInfo().IsAbstract)
                .Select(vertexKvp => vertexKvp.Key
                    .TryGetPartitionKeyExpression(model)
                    .Map(keyExpression => ((keyExpression as LambdaExpression)?.Body as MemberExpression)?.Member.Name)
                    .AsEnumerable()
                    .Aggregate(
                        GremlinQuery
                            .Create("schema", queryProvider)
                            .AddStep<string>("vertexLabel", vertexKvp.Value),
                        (closureQuery, property) => closureQuery.AddStep<string>("partitionKey", property))
                    .ConditionalAddStep(
                        vertexKvp.Key.GetProperties().Any(),
                        query => query.AddStep<string>(
                            "properties",
                            vertexKvp.Key
                                .GetProperties()
                                .Select(x => x.Name)
                                .ToImmutableList<object>()))
                    .AddStep<string>("create"));
        }

        private static IEnumerable<IGremlinQuery<string>> CreateEdgeLabelQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.EdgeLabels
                .Where(edgeKvp => !edgeKvp.Key.GetTypeInfo().IsAbstract)
                .Select(edgeKvp => model.Connections
                    .TryGetValue(edgeKvp.Key)
                    .AsEnumerable()
                    .SelectMany(x => x)
                    .Where(x => !x.Item1.GetTypeInfo().IsAbstract && !x.Item2.GetTypeInfo().IsAbstract)
                    .Aggregate(
                        GremlinQuery
                            .Create("schema", queryProvider)
                            .AddStep<string>("edgeLabel", edgeKvp.Value)
                            .AddStep<string>("single")
                            .ConditionalAddStep(
                                edgeKvp.Key
                                    .GetProperties()
                                    .Any(),
                                query => query.AddStep<string>(
                                    "properties",
                                    edgeKvp.Key
                                        .GetProperties()
                                        .Select(property => property.Name)
                                        .ToImmutableList<object>())),
                        (closureQuery, tuple) => closureQuery.AddStep<string>(
                            "connection",
                            model.VertexLabels.TryGetValue(tuple.Item1).IfNone(() => throw new InvalidOperationException(/* TODO: Message */ )),
                            model.VertexLabels.TryGetValue(tuple.Item2).IfNone(() => throw new InvalidOperationException(/* TODO: Message */ ))))
                    .AddStep<string>("ifNotExists")
                    .AddStep<string>("create"));
        }

        private static IEnumerable<IGremlinQuery<string>> CreateEdgeIndexQueries(this IDseGraphModel model, IGremlinQueryProvider queryProvider)
        {
            return model.EdgeIndexes.Keys
                .SelectMany(type => type
                    .GetTypeHierarchy(model)
                    .Where(inheritedType => !inheritedType.GetTypeInfo().IsAbstract)
                    .SelectMany(inheritedType => model
                        .EdgeIndexes[inheritedType]
                        .Where(index => index.direction != EdgeDirection.None)
                        .SelectMany(index => index.vertexType
                            .GetTypeHierarchy(model)
                            .Where(inheritedVertexType => !inheritedVertexType.GetTypeInfo().IsAbstract)
                            .Select(inheritedVertexType => GremlinQuery
                                .Create("schema", queryProvider)
                                .AddStep<string>("vertexLabel", inheritedVertexType.Name)
                                .AddStep<string>("index", Guid.NewGuid().ToString("N"))
                                .AddStep<string>(
                                    index.direction == EdgeDirection.Out
                                        ? "outE" 
                                        : index.direction == EdgeDirection.In
                                            ? "inE"
                                            : "bothE",
                                    type.Name)
                                .AddStep<string>("by", ((index.indexExpression as LambdaExpression)?.Body as MemberExpression)?.Member.Name)))));
        }


        private static IDseGraphModel AddConnection(this IDseGraphModel model, Type outVertexType, Type edgeType, Type inVertexType)
        {
            model.VertexLabels
                .TryGetValue(outVertexType)
                .IfNone(() => throw new ArgumentException($"Model does not contain vertex type {outVertexType}."));

            model.VertexLabels
                .TryGetValue(inVertexType)
                .IfNone(() => throw new ArgumentException($"Model does not contain vertex type {inVertexType}."));

            model.EdgeLabels
                .TryGetValue(edgeType)
                .IfNone(() => throw new ArgumentException($"Model does not contain edge type {edgeType}."));

            var newConnections = model.Connections.Add(edgeType, (outVertexType, inVertexType));

            return newConnections != model.Connections
                ? new DseGraphModel(
                    model.VertexLabels, 
                    model.EdgeLabels, 
                    newConnections, 
                    model.PrimaryKeys, 
                    model.MaterializedIndexes, 
                    model.SecondaryIndexes, 
                    model.SearchIndexes,
                    model.EdgeIndexes)
                : model;
        }

        private static IGremlinQuery<TSource> ConditionalAddStep<TSource>(this IGremlinQuery<TSource> query, bool condition, Func<IGremlinQuery<TSource>, IGremlinQuery<TSource>> addStepFunction)
        {
            return condition ? addStepFunction(query) : query;
        }

        private static Option<Expression> TryGetPartitionKeyExpression(this Type vertexType, IDseGraphModel model)
        {
            return vertexType
                .GetTypeHierarchy(model)
                .SelectMany(type => model.PrimaryKeys
                    .TryGetValue(type)
                    .AsEnumerable())
                .FirstOrDefault();
        }

        private static IEnumerable<Type> GetTypeHierarchy(this Type type, IGraphModel model)
        {
            while (type != null && model.VertexLabels.ContainsKey(type) || model.EdgeLabels.ContainsKey(type))
            {
                yield return type;
                type = type.GetTypeInfo().BaseType;
            }
        }
    }
}