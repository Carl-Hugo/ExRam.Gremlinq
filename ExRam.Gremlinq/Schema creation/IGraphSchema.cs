﻿using System.Collections.Immutable;

namespace ExRam.Gremlinq
{
    public interface IGraphSchema
    {
        ImmutableList<VertexSchemaInfo> VertexSchemaInfos { get; }
        ImmutableList<EdgeSchemaInfo> EdgeSchemaInfos { get; }
        ImmutableList<PropertySchemaInfo> PropertySchemaInfos { get; }
        ImmutableList<(string, string, string)> Connections { get; }
    }
}