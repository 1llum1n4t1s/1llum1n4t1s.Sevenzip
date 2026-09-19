/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/* ------------------------------------------------------------------------- */
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// CompressionOptionSetterTest
///
/// <summary>
/// CompressionOptionSetter の回帰テスト。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class CompressionOptionSetterTest
{
    /* --------------------------------------------------------------------- */
    ///
    /// CustomParameterOverridesKnownStringProperty
    ///
    /// <summary>
    /// 既知の文字列プロパティと同名のカスタム値が優先されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(Format.Zip, "m", "Copy")]
    [TestCase(Format.Zip, "em", "ZipCrypto")]
    [TestCase(Format.SevenZip, "0", "Copy")]
    public void CustomParameterOverridesKnownStringProperty(
        Format format,
        string key,
        string expected)
    {
        var options = new CompressionOption
        {
            CompressionMethod = CompressionMethod.Lzma,
            EncryptionMethod = EncryptionMethod.Aes256,
            CustomParameters = new Dictionary<string, string> { [key] = expected },
        };
        var destination = new PropertiesStub();

        CompressionOptionSetter.From(format, options).Invoke(destination);

        Assert.That(destination.Values[key], Is.EqualTo(expected));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SetPropertiesFailurePropagates
    ///
    /// <summary>
    /// ISetProperties の失敗 HRESULT が呼び出し元へ通知されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SetPropertiesFailurePropagates()
    {
        const int hr = unchecked((int)0x80070057);
        var destination = new PropertiesStub(hr);
        var setter = new ZipOptionSetter(new CompressionOption());

        var error = Assert.Throws<IOException>(() => setter.Invoke(destination));
        Assert.That(error.Message, Does.Contain("0x80070057"));
        Assert.That(error.HResult, Is.EqualTo(hr));
    }

    private sealed class PropertiesStub : ISetProperties
    {
        public PropertiesStub(int result = 0) => _result = result;

        public IDictionary<string, object> Values { get; } =
            new Dictionary<string, object>();

        public int SetProperties(string[] names, IntPtr values, uint numProperties)
        {
            var size = Marshal.SizeOf<PropVariant>();
            for (var i = 0; i < numProperties; i++)
            {
                var value = Marshal.PtrToStructure<PropVariant>(
                    IntPtr.Add(values, checked((int)i) * size));
                Values[names[i]] = value.Object;
            }
            return _result;
        }

        private readonly int _result;
    }
}
