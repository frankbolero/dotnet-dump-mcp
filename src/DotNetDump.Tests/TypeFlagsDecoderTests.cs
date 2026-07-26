using System.Reflection;

using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

public class TypeFlagsDecoderTests {
	[Fact]
	public void IsInterface_RequiresTheClassSemanticsBit() {
		Assert.True(TypeFlagsDecoder.IsInterface(TypeAttributes.Interface | TypeAttributes.Abstract));
		Assert.False(TypeFlagsDecoder.IsInterface(TypeAttributes.Class));
		Assert.False(TypeFlagsDecoder.IsInterface(TypeAttributes.Public | TypeAttributes.Sealed));
	}

	[Fact]
	public void IsAbstract_DetectsTheAbstractBit() {
		Assert.True(TypeFlagsDecoder.IsAbstract(TypeAttributes.Abstract));
		Assert.True(TypeFlagsDecoder.IsAbstract(TypeAttributes.Public | TypeAttributes.Abstract));
		Assert.False(TypeFlagsDecoder.IsAbstract(TypeAttributes.Public));
	}

	[Fact]
	public void IsSealed_DetectsTheSealedBit() {
		Assert.True(TypeFlagsDecoder.IsSealed(TypeAttributes.Sealed));
		Assert.False(TypeFlagsDecoder.IsSealed(TypeAttributes.Public));
	}

	[Fact]
	public void AStaticClass_IsBothAbstractAndSealed() {
		// This is how a C# static class is encoded, and previously reported as neither.
		var attributes = TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Public;

		Assert.True(TypeFlagsDecoder.IsAbstract(attributes));
		Assert.True(TypeFlagsDecoder.IsSealed(attributes));
		Assert.False(TypeFlagsDecoder.IsInterface(attributes));
	}

	[Theory]
	[InlineData(TypeAttributes.Public, "public")]
	[InlineData(TypeAttributes.NotPublic, "internal")]
	[InlineData(TypeAttributes.NestedPublic, "nested public")]
	[InlineData(TypeAttributes.NestedPrivate, "nested private")]
	[InlineData(TypeAttributes.NestedFamily, "nested protected")]
	[InlineData(TypeAttributes.NestedAssembly, "nested internal")]
	public void Visibility_MapsTheVisibilityMask(TypeAttributes attributes, string expected) {
		Assert.Equal(expected, TypeFlagsDecoder.Visibility(attributes));
	}

	[Fact]
	public void Visibility_IgnoresUnrelatedBits() {
		var attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
		Assert.Equal("public", TypeFlagsDecoder.Visibility(attributes));
	}

	[Fact]
	public void IsNested_DistinguishesNestedFromTopLevel() {
		Assert.True(TypeFlagsDecoder.IsNested(TypeAttributes.NestedPrivate));
		Assert.False(TypeFlagsDecoder.IsNested(TypeAttributes.Public));
		Assert.False(TypeFlagsDecoder.IsNested(TypeAttributes.NotPublic));
	}

	[Fact]
	public void RealTypesFromThisAssembly_AreClassifiedCorrectly() {
		// Cross-check the decoder against types the runtime already agrees about.
		Assert.True(TypeFlagsDecoder.IsInterface(typeof(IDisposable).Attributes));
		Assert.True(TypeFlagsDecoder.IsSealed(typeof(string).Attributes));
		Assert.True(TypeFlagsDecoder.IsAbstract(typeof(Stream).Attributes));
		Assert.False(TypeFlagsDecoder.IsInterface(typeof(string).Attributes));
	}
}