using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using LanguageExt;

namespace ExRam.Gremlinq.Dse
{
    public static class DseGraphSchemaExtensions
    {
        public static DseGraphSchema ToGraphSchema(this IGraphModel model)
        {
            var schema = new DseGraphSchema(model, ImmutableList<VertexSchemaInfo>.Empty, ImmutableList<EdgeSchemaInfo>.Empty, ImmutableList<PropertySchemaInfo>.Empty, ImmutableList<(string, string, string)>.Empty);
            var propertyKeys = new Dictionary<string, Type>();

            model = model.EdgeConnectionClosure();

            foreach (var vertexType in model.VertexTypes.Values.Cast<GraphElementInfo>().Concat(model.EdgeTypes.Values))
            {
                foreach (var property in vertexType.ElementType.GetProperties())
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

            schema = propertyKeys
                .Aggregate(
                    schema,
                    (closureSchema, propertyKvp) => closureSchema.Property(propertyKvp.Key, propertyKvp.Value));

            schema = model.VertexTypes.Values
                .Where(x => !x.ElementType.GetTypeInfo().IsAbstract)
                .Aggregate(
                    schema,
                    (closureSchema, vertexType) => closureSchema.VertexLabel(
                        vertexType,
                        vertexType
                            .TryGetPartitionKeyExpression(model)
                            .Map(keyExpression => ((keyExpression as LambdaExpression)?.Body as MemberExpression)?.Member
                                .Name)
                            .AsEnumerable()
                            .ToImmutableList(),
                        model
                            .GetElementInfoHierarchy(vertexType)
                            .OfType<VertexTypeInfo>()
                            .SelectMany(x => x.SecondaryIndexes)
                            .Select(indexExpression => ((indexExpression as LambdaExpression)?.Body.StripConvert() as MemberExpression)?.Member.Name)
                            .ToImmutableList()));

            schema = model.EdgeTypes.Values
                .Where(x => !x.ElementType.GetTypeInfo().IsAbstract)
                .Aggregate(
                    schema,
                    (closureSchema, edgeType) => closureSchema.EdgeLabel(
                        edgeType.Label,
                        edgeType.ElementType.GetProperties().Select(property => property.Name).ToImmutableList()));

            return model.Connections
                .Where(x => !x.Item1.GetTypeInfo().IsAbstract && !x.Item2.GetTypeInfo().IsAbstract && !x.Item3.GetTypeInfo().IsAbstract)
                .Aggregate(
                    schema,
                    (closureSchema, connectionTuple) => closureSchema.Connection(
                        model.TryGetLabelOfType(connectionTuple.Item1).IfNone(() => throw new InvalidOperationException(/* TODO: Better exception */)),
                        model.TryGetLabelOfType(connectionTuple.Item2).IfNone(() => throw new InvalidOperationException(/* TODO: Better exception */)),
                        model.TryGetLabelOfType(connectionTuple.Item3).IfNone(() => throw new InvalidOperationException(/* TODO: Better exception */))));
        }

        public static DseGraphSchema Property<T>(this DseGraphSchema schema, string name)
        {
            return schema.Property(name, typeof(T));
        }

        public static DseGraphSchema Property(this DseGraphSchema schema, string name, Type type)
        {
            return new DseGraphSchema(schema.Model, schema.VertexSchemaInfos, schema.EdgeSchemaInfos, schema.PropertySchemaInfos.Add(new PropertySchemaInfo(name, type)), schema.Connections);
        }

        public static DseGraphSchema VertexLabel(this DseGraphSchema schema, VertexTypeInfo typeInfo, ImmutableList<string> partitionKeyProperties, ImmutableList<string> indexProperties)
        {
            return new DseGraphSchema(schema.Model, schema.VertexSchemaInfos.Add(new VertexSchemaInfo(typeInfo, partitionKeyProperties, indexProperties)), schema.EdgeSchemaInfos, schema.PropertySchemaInfos, schema.Connections);
        }

        public static DseGraphSchema EdgeLabel(this DseGraphSchema schema, string label, ImmutableList<string> properties)
        {
            return new DseGraphSchema(schema.Model, schema.VertexSchemaInfos, schema.EdgeSchemaInfos.Add(new EdgeSchemaInfo(label, properties)), schema.PropertySchemaInfos, schema.Connections);
        }

        public static DseGraphSchema Connection(this DseGraphSchema schema, string outVertexLabel, string edgeLabel, string inVertexLabel)
        {
            return new DseGraphSchema(schema.Model, schema.VertexSchemaInfos, schema.EdgeSchemaInfos, schema.PropertySchemaInfos, schema.Connections.Add((outVertexLabel, edgeLabel, inVertexLabel)));
        }

        private static Option<Expression> TryGetPartitionKeyExpression(this VertexTypeInfo vertexTypeInfo, IGraphModel model)
        {
            return vertexTypeInfo.PrimaryKey
                .Match(
                    _ => (Option<Expression>)_,
                    () =>
                    {
                        var baseType = vertexTypeInfo.ElementType.GetTypeInfo().BaseType;

                        if (baseType != null)
                        {
                            return model.VertexTypes
                                .TryGetValue(baseType)
                                .Bind(baseVertexInfo => baseVertexInfo.TryGetPartitionKeyExpression(model));
                        }

                        return Option<Expression>.None;
                    });
        }

        private static IEnumerable<GraphElementInfo> GetElementInfoHierarchy(this IGraphModel model, GraphElementInfo elementInfo)
        {
            do
            {
                yield return elementInfo;
                var baseType = elementInfo.ElementType.GetTypeInfo().BaseType;

                elementInfo = null;

                if (model.VertexTypes.TryGetValue(baseType, out var vertexInfo))
                    elementInfo = vertexInfo;
                else if (model.EdgeTypes.TryGetValue(baseType, out var edgeInfo))
                    elementInfo = edgeInfo;
            } while (elementInfo != null);
        }
    }
}