using LanguageExt;
using System;
using System.Collections.Generic;

namespace ExRam.Gremlinq
{
    public abstract class AddElementGremlinStep : NonTerminalGremlinStep
    {
        private readonly object _value;
        private readonly string _stepName;

        protected AddElementGremlinStep(string stepName, object value)
        {
            this._value = value;
            this._stepName = stepName;
        }

        public override IEnumerable<TerminalGremlinStep> Resolve(IGraphModel model)
        {
            var type = this._value.GetType();
            
            yield return new TerminalGremlinStep(
                this._stepName,
                model
                    .TryGetLabelOfType(type)
                    .IfNone(type.Name));
        }
    }

    public class StringGremlinStep : NonTerminalGremlinStep
    {
        private readonly string _name;
        private readonly string _value;

        public StringGremlinStep(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentNullException(nameof(name)); }
            if (string.IsNullOrWhiteSpace(value)) { throw new ArgumentNullException(nameof(value)); }
            _value = value;
            _name = name;
        }

        public override IEnumerable<TerminalGremlinStep> Resolve(IGraphModel model)
        {
            yield return new TerminalGremlinStep(
                _name,
                _value //new SpecialGremlinString(_value)
            );
        }

    }

    public sealed class AddEStringGremlinStep : StringGremlinStep
    {
        public AddEStringGremlinStep(string value)
            : base("addE", value)
        {
        }
    }

    public sealed class OutEStringGremlinStep : StringGremlinStep
    {
        public OutEStringGremlinStep(object value)
            : base("outE", value.GetType().Name)
        {
        }
    }

}