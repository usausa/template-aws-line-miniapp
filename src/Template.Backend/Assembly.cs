using Amazon.Lambda.Serialization.SystemTextJson;

using Template.Backend;

[assembly: System.CLSCompliant(false)]

// Source-generated serialization keeps the handlers free of reflection-based JSON.
[assembly: LambdaSerializer(typeof(SourceGeneratorLambdaJsonSerializer<FunctionSerializerContext>))]
