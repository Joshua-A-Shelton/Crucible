using Crucible.Core;
using Crucible.Core.ECS;
using Crucible.Core.Exceptions;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class ECSTests
{
    public static TestResult GetDataTypeIdTest()
    {
        var v3Id = DataTypeId.For<Vector3>();
        var v3Id2 = DataTypeId.For<Vector3>();
        var v2Id = DataTypeId.For<Vector2>();
        if (v3Id == v3Id2 && v3Id != v2Id)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Data Types are not unique to type");
    }

    public static TestResult GetReferenceTypeIdTest()
    {
        var gameManagerId = ReferenceTypeId.For<Texture2D>();
        var gameManagerId2 = ReferenceTypeId.For<Texture2D>();
        var exceptionId = ReferenceTypeId.For<ComponentNotFoundException>();
        if (gameManagerId == gameManagerId2 && gameManagerId != exceptionId)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Referece types are not unique to type");
    }
}