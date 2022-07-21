/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) Rick Brewster, Tom Jackson, and past contributors.            //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See src/Setup/License.txt for full licensing and attribution details.       //
// 2                                                                           //
// 1                                                                           //
/////////////////////////////////////////////////////////////////////////////////

#include "GeneratedCodeWarning.h"

using System;

// The generalized alpha compositing formula, "B OVER A" is:
// C(A,a,B,b) = bB + aA - baA
// where:
//    A = background color value
//    a = background alpha value
//    B = foreground color value
//    b = foreground alpha value
//
// However, we need a general formula for composition based on any type of
// blend operation and not just for 'normal' blending. We want multiplicative,
// additive, etc. blend operations.
//
// The generalized alpha compositing formula w.r.t. a replaceable blending
// function is:
//
// G(A,a,B,b,F) = (a - ab)A + (b - ab)B + abF(A, B)
// 
// Where F is a function of A and B, or F(A,B), that results in another color
// value. For A OVER B blending, we simply use F(A,B) = B. It can be easily 
// shown that the two formulas simplify to the same expression when this F is 
// used.
//
// G can be generalized even further to take a function for the other input
// values. This can be useful if one wishes to implement something like 
// (1 - B) OVER A blending.
//
// In this reality, F(A,B) is really F(A,B,r). The syntax "r = F(A,B)" is
// the same as "F(A,B,r)" where r is essentially an 'out' parameter.


// Multiplies a and b, which are [0,255] as if they were scaled to [0,1], and returns the result in r
// a and b are evaluated once. r is evaluated multiple times.
#define INT_SCALE_MULT(a, b) ((a) * (b) + 0x80)
#define INT_SCALE_DIV(r) ((((r) >> 8) + (r)) >> 8)
#define INT_SCALE(a, b, r) { r = INT_SCALE_MULT(a, b); r = INT_SCALE_DIV(r); }

#define COMPUTE_ALPHA(a, b, r) { INT_SCALE(a, 255 - (b), r); r += (b); }

// F(A,B) = blending function for the pixel values
// h(a) = function for loading lhs.A, usually just ID
// j(a) = function for loading rhs.A, usually just ID

#define BLEND(lhs, rhs, F, h, j) \
                int lhsA; \
                h((lhs).A, lhsA); \
                int rhsA; \
                j((rhs).A, rhsA); \
                int y; \
                INT_SCALE(lhsA, 255 - rhsA, y); \
                int totalA = y + rhsA; \
                uint ret; \
 \
                if (totalA == 0) \
                { \
                    ret = 0; \
                } \
                else \
                { \
                    int fB; \
                    int fG; \
                    int fR; \
 \
                    F((lhs).B, (rhs).B, fB); \
                    F((lhs).G, (rhs).G, fG); \
                    F((lhs).R, (rhs).R, fR); \
 \
                    int x; \
                    INT_SCALE(lhsA, rhsA, x); \
                    int z = rhsA - x; \
 \
                    int masIndex = totalA * 3; \
                    uint taM = masTable[masIndex]; \
                    uint taA = masTable[masIndex + 1]; \
                    uint taS = masTable[masIndex + 2]; \
 \
                    uint b = (uint)(((((long)((((lhs).B * y) + ((rhs).B * z) + (fB * x)))) * taM) + taA) >> (int)taS); \
                    uint g = (uint)(((((long)((((lhs).G * y) + ((rhs).G * z) + (fG * x)))) * taM) + taA) >> (int)taS); \
                    uint r = (uint)(((((long)((((lhs).R * y) + ((rhs).R * z) + (fR * x)))) * taM) + taA) >> (int)taS); \
                    int a; \
                    COMPUTE_ALPHA(lhsA, rhsA, a); \
 \
                    ret = b + (g << 8) + (r << 16) + ((uint)a << 24); \
                } \

#define IMPLEMENT_INSTANCE_FUNCTIONS(F, h, j) \
            public override ColorBgra Apply(ColorBgra lhs, ColorBgra rhs) \
            { \
                BLEND(lhs, rhs, F, h, j); \
                return ColorBgra.FromUInt32(ret); \
            } \
 \
            public unsafe override void Apply(ColorBgra *dst, ColorBgra *src, int length) \
            { \
                while (length > 0) \
                { \
                    BLEND(*dst, *src, F, h, j); \
                    dst->Bgra = ret; \
                    ++dst; \
                    ++src; \
                    --length; \
                } \
            } \
 \
            public unsafe override void Apply(ColorBgra *dst, ColorBgra *lhs, ColorBgra *rhs, int length) \
            { \
                while (length > 0) \
                { \
                    BLEND(*lhs, *rhs, F, h, j); \
                    dst->Bgra = ret; \
                    ++dst; \
                    ++lhs; \
                    ++rhs; \
                    --length; \
                } \
            }

#define IMPLEMENT_STATIC_FUNCTIONS(F, h, j) \
            public static ColorBgra ApplyStatic(ColorBgra lhs, ColorBgra rhs) \
            { \
                BLEND(lhs, rhs, F, h, j); \
                return ColorBgra.FromUInt32(ret); \
            }

#define IMPLEMENT_OP_STATICNAME(NAME) \
            public static string StaticName \
            { \
                get \
                { \
                    return PdnResources.GetString("UserBlendOps." + #NAME + "BlendOp.Name"); \
                } \
            } \

#define ID(x, r) { r = (x); }
#define ALPHA_WITH_OPACITY(a, r) INT_SCALE(a, this.opacity, r)
#define APPLY_OPACITY_ADAPTER(a, r) { r = ApplyOpacity(a); }

#define DEFINE_OP(NAME, PROTECTION, F, h, j) \
        [Serializable] \
        PROTECTION sealed class NAME##BlendOp \
            : UserBlendOp \
        { \
            IMPLEMENT_OP_STATICNAME(NAME) \
            IMPLEMENT_INSTANCE_FUNCTIONS(F, h, j) \
            IMPLEMENT_STATIC_FUNCTIONS(F, h, j) \
 \
            public override UserBlendOp CreateWithOpacity(int opacity) \
            { \
                return new NAME##BlendOpWithOpacity(opacity); \
            } \
 \
            private sealed class NAME##BlendOpWithOpacity \
                : UserBlendOp \
            { \
                private int opacity; \
 \
                private byte ApplyOpacity(byte a) \
                { \
                    int r; \
                    j(a, r); \
                    ALPHA_WITH_OPACITY(r, r); \
                    return (byte)r; \
                } \
 \
                IMPLEMENT_OP_STATICNAME(NAME) \
 \
                IMPLEMENT_INSTANCE_FUNCTIONS(F, h, APPLY_OPACITY_ADAPTER) \
 \
                public NAME##BlendOpWithOpacity(int opacity) \
                { \
                    if (this.opacity < 0 || this.opacity > 255) \
                    { \
                        throw new ArgumentOutOfRangeException(); \
                    } \
 \
                    this.opacity = opacity; \
                } \
            } \
        }

#define DEFINE_STANDARD_OP(NAME, F) DEFINE_OP(NAME, public, F, ID, ID)

#define OVER(A, B, r) \
{ \
    r = (B); \
}

#define MULTIPLY(A, B, r) \
{ \
    INT_SCALE((A), (B), r); \
}

#define MIN(A, B, r) \
{ \
    r = Math.Min(A, B); \
}

#define MAX(A, B, r) \
{ \
    r = Math.Max(A, B); \
}

#define SATURATE(A, B, r) \
{ \
    r = Math.Min(255, (A) + (B)); \
}

// n DIV d
#define INT_DIV(n, d, r) \
{ \
    int i = (d) * 3; \
    uint M = masTable[i]; \
    uint A = masTable[i + 1]; \
    uint S = masTable[i + 2]; \
    r = (int)(((n * M) + A) >> (int)S); \
}

//{ r = (((B) == 0) ? 0 : Math.Max(0, (255 - (((255 - (A)) * 255) / (B))))); }
#define COLORBURN(A, B, r) \
{  \
    if ((B) == 0) \
    { \
        r = 0; \
    } \
    else \
    { \
        INT_DIV(((255 - (A)) * 255), (B), r); \
        r = 255 -  r; \
        r = Math.Max(0, r); \
    } \
}

// { r = ((B) == 255 ? 255 : Math.Min(255, ((A) * 255) / (255 - (B)))); }
#define COLORDODGE(A, B, r) \
{ \
    if ((B) == 255) \
    { \
        r = 255; \
    } \
    else \
    { \
        INT_DIV(((A) * 255), (255 - (B)), r); \
        r = Math.Min(255, r); \
    } \
}    

// r = { (((B) == 255) ? 255 : Math.Min(255, ((A) * (A)) / (255 - (B)))); }
#define REFLECT(A, B, r) \
{ \
    if ((B) == 255) \
    { \
        r = 255; \
    } \
    else \
    { \
        INT_DIV((A) * (A), 255 - (B), r); \
        r = Math.Min(255, r); \
    } \
}

#define GLOW(A, B, r) REFLECT(B, A, r)

#define OVERLAY(A, B, r) \
{  \
    if ((A) < 128) \
    { \
        INT_SCALE(2 * (A), (B), r); \
    } \
    else \
    { \
        INT_SCALE(2 * (255 - (A)), 255 - (B), r); \
        r = 255 - r; \
    } \
}

#define DIFFERENCE(A, B, r) \
{ \
    r = Math.Abs((B) - (A)); \
}

#define NEGATION(A, B, r) \
{ \
    r = (255 - Math.Abs(255 - (A) - (B))); \
}

//{ r = ((B) + (A) - (((B) * (A)) / 255)); }
#define SCREEN(A, B, r) \
{ \
    INT_SCALE((B), (A), r); \
    r = (B) + (A) - r; \
}

#define XOR(A, B, r) \
{ \
    r = ((A) ^ (B)); \
}

namespace PaintDotNet
{
    partial class UserBlendOps
    {
        // i = z * 3;
        // (x / z) = ((x * masTable[i]) + masTable[i + 1]) >> masTable[i + 2)
        private static readonly uint[] masTable = 
        {
            0x00000000, 0x00000000, 0,  // 0
            0x00000001, 0x00000000, 0,  // 1
            0x00000001, 0x00000000, 1,  // 2
            0xAAAAAAAB, 0x00000000, 33, // 3
            0x00000001, 0x00000000, 2,  // 4
            0xCCCCCCCD, 0x00000000, 34, // 5
            0xAAAAAAAB, 0x00000000, 34, // 6
            0x49249249, 0x49249249, 33, // 7
            0x00000001, 0x00000000, 3,  // 8
            0x38E38E39, 0x00000000, 33, // 9
            0xCCCCCCCD, 0x00000000, 35, // 10
            0xBA2E8BA3, 0x00000000, 35, // 11
            0xAAAAAAAB, 0x00000000, 35, // 12
            0x4EC4EC4F, 0x00000000, 34, // 13
            0x49249249, 0x49249249, 34, // 14
            0x88888889, 0x00000000, 35, // 15
            0x00000001, 0x00000000, 4,  // 16
            0xF0F0F0F1, 0x00000000, 36, // 17
            0x38E38E39, 0x00000000, 34, // 18
            0xD79435E5, 0xD79435E5, 36, // 19
            0xCCCCCCCD, 0x00000000, 36, // 20
            0xC30C30C3, 0xC30C30C3, 36, // 21
            0xBA2E8BA3, 0x00000000, 36, // 22
            0xB21642C9, 0x00000000, 36, // 23
            0xAAAAAAAB, 0x00000000, 36, // 24
            0x51EB851F, 0x00000000, 35, // 25
            0x4EC4EC4F, 0x00000000, 35, // 26
            0x97B425ED, 0x97B425ED, 36, // 27
            0x49249249, 0x49249249, 35, // 28
            0x8D3DCB09, 0x00000000, 36, // 29
            0x88888889, 0x00000000, 36, // 30
            0x42108421, 0x42108421, 35, // 31
            0x00000001, 0x00000000, 5,  // 32
            0x3E0F83E1, 0x00000000, 35, // 33
            0xF0F0F0F1, 0x00000000, 37, // 34
            0x75075075, 0x75075075, 36, // 35
            0x38E38E39, 0x00000000, 35, // 36
            0x6EB3E453, 0x6EB3E453, 36, // 37
            0xD79435E5, 0xD79435E5, 37, // 38
            0x69069069, 0x69069069, 36, // 39
            0xCCCCCCCD, 0x00000000, 37, // 40
            0xC7CE0C7D, 0x00000000, 37, // 41
            0xC30C30C3, 0xC30C30C3, 37, // 42
            0x2FA0BE83, 0x00000000, 35, // 43
            0xBA2E8BA3, 0x00000000, 37, // 44
            0x5B05B05B, 0x5B05B05B, 36, // 45
            0xB21642C9, 0x00000000, 37, // 46
            0xAE4C415D, 0x00000000, 37, // 47
            0xAAAAAAAB, 0x00000000, 37, // 48
            0x5397829D, 0x00000000, 36, // 49
            0x51EB851F, 0x00000000, 36, // 50
            0xA0A0A0A1, 0x00000000, 37, // 51
            0x4EC4EC4F, 0x00000000, 36, // 52
            0x9A90E7D9, 0x9A90E7D9, 37, // 53
            0x97B425ED, 0x97B425ED, 37, // 54
            0x94F2094F, 0x94F2094F, 37, // 55
            0x49249249, 0x49249249, 36, // 56
            0x47DC11F7, 0x47DC11F7, 36, // 57
            0x8D3DCB09, 0x00000000, 37, // 58
            0x22B63CBF, 0x00000000, 35, // 59
            0x88888889, 0x00000000, 37, // 60
            0x4325C53F, 0x00000000, 36, // 61
            0x42108421, 0x42108421, 36, // 62
            0x41041041, 0x41041041, 36, // 63
            0x00000001, 0x00000000, 6,  // 64
            0xFC0FC0FD, 0x00000000, 38, // 65
            0x3E0F83E1, 0x00000000, 36, // 66
            0x07A44C6B, 0x00000000, 33, // 67
            0xF0F0F0F1, 0x00000000, 38, // 68
            0x76B981DB, 0x00000000, 37, // 69
            0x75075075, 0x75075075, 37, // 70
            0xE6C2B449, 0x00000000, 38, // 71
            0x38E38E39, 0x00000000, 36, // 72
            0x381C0E07, 0x381C0E07, 36, // 73
            0x6EB3E453, 0x6EB3E453, 37, // 74
            0x1B4E81B5, 0x00000000, 35, // 75
            0xD79435E5, 0xD79435E5, 38, // 76
            0x3531DEC1, 0x00000000, 36, // 77
            0x69069069, 0x69069069, 37, // 78
            0xCF6474A9, 0x00000000, 38, // 79
            0xCCCCCCCD, 0x00000000, 38, // 80
            0xCA4587E7, 0x00000000, 38, // 81
            0xC7CE0C7D, 0x00000000, 38, // 82
            0x3159721F, 0x00000000, 36, // 83
            0xC30C30C3, 0xC30C30C3, 38, // 84
            0xC0C0C0C1, 0x00000000, 38, // 85
            0x2FA0BE83, 0x00000000, 36, // 86
            0x2F149903, 0x00000000, 36, // 87
            0xBA2E8BA3, 0x00000000, 38, // 88
            0xB81702E1, 0x00000000, 38, // 89
            0x5B05B05B, 0x5B05B05B, 37, // 90
            0x2D02D02D, 0x2D02D02D, 36, // 91
            0xB21642C9, 0x00000000, 38, // 92
            0xB02C0B03, 0x00000000, 38, // 93
            0xAE4C415D, 0x00000000, 38, // 94
            0x2B1DA461, 0x2B1DA461, 36, // 95
            0xAAAAAAAB, 0x00000000, 38, // 96
            0xA8E83F57, 0xA8E83F57, 38, // 97
            0x5397829D, 0x00000000, 37, // 98
            0xA57EB503, 0x00000000, 38, // 99
            0x51EB851F, 0x00000000, 37, // 100
            0xA237C32B, 0xA237C32B, 38, // 101
            0xA0A0A0A1, 0x00000000, 38, // 102
            0x9F1165E7, 0x9F1165E7, 38, // 103
            0x4EC4EC4F, 0x00000000, 37, // 104
            0x27027027, 0x27027027, 36, // 105
            0x9A90E7D9, 0x9A90E7D9, 38, // 106
            0x991F1A51, 0x991F1A51, 38, // 107
            0x97B425ED, 0x97B425ED, 38, // 108
            0x2593F69B, 0x2593F69B, 36, // 109
            0x94F2094F, 0x94F2094F, 38, // 110
            0x24E6A171, 0x24E6A171, 36, // 111
            0x49249249, 0x49249249, 37, // 112
            0x90FDBC09, 0x90FDBC09, 38, // 113
            0x47DC11F7, 0x47DC11F7, 37, // 114
            0x8E78356D, 0x8E78356D, 38, // 115
            0x8D3DCB09, 0x00000000, 38, // 116
            0x23023023, 0x23023023, 36, // 117
            0x22B63CBF, 0x00000000, 36, // 118
            0x44D72045, 0x00000000, 37, // 119
            0x88888889, 0x00000000, 38, // 120
            0x8767AB5F, 0x8767AB5F, 38, // 121
            0x4325C53F, 0x00000000, 37, // 122
            0x85340853, 0x85340853, 38, // 123
            0x42108421, 0x42108421, 37, // 124
            0x10624DD3, 0x00000000, 35, // 125
            0x41041041, 0x41041041, 37, // 126
            0x10204081, 0x10204081, 35, // 127
            0x00000001, 0x00000000, 7,  // 128
            0x0FE03F81, 0x00000000, 35, // 129
            0xFC0FC0FD, 0x00000000, 39, // 130
            0xFA232CF3, 0x00000000, 39, // 131
            0x3E0F83E1, 0x00000000, 37, // 132
            0xF6603D99, 0x00000000, 39, // 133
            0x07A44C6B, 0x00000000, 34, // 134
            0xF2B9D649, 0x00000000, 39, // 135
            0xF0F0F0F1, 0x00000000, 39, // 136
            0x077975B9, 0x00000000, 34, // 137
            0x76B981DB, 0x00000000, 38, // 138
            0x75DED953, 0x00000000, 38, // 139
            0x75075075, 0x75075075, 38, // 140
            0x3A196B1F, 0x00000000, 37, // 141
            0xE6C2B449, 0x00000000, 39, // 142
            0xE525982B, 0x00000000, 39, // 143
            0x38E38E39, 0x00000000, 37, // 144
            0xE1FC780F, 0x00000000, 39, // 145
            0x381C0E07, 0x381C0E07, 37, // 146
            0xDEE95C4D, 0x00000000, 39, // 147
            0x6EB3E453, 0x6EB3E453, 38, // 148
            0xDBEB61EF, 0x00000000, 39, // 149
            0x1B4E81B5, 0x00000000, 36, // 150
            0x36406C81, 0x00000000, 37, // 151
            0xD79435E5, 0xD79435E5, 39, // 152
            0xD62B80D7, 0x00000000, 39, // 153
            0x3531DEC1, 0x00000000, 37, // 154
            0xD3680D37, 0x00000000, 39, // 155
            0x69069069, 0x69069069, 38, // 156
            0x342DA7F3, 0x00000000, 37, // 157
            0xCF6474A9, 0x00000000, 39, // 158
            0xCE168A77, 0xCE168A77, 39, // 159
            0xCCCCCCCD, 0x00000000, 39, // 160
            0xCB8727C1, 0x00000000, 39, // 161
            0xCA4587E7, 0x00000000, 39, // 162
            0xC907DA4F, 0x00000000, 39, // 163
            0xC7CE0C7D, 0x00000000, 39, // 164
            0x634C0635, 0x00000000, 38, // 165
            0x3159721F, 0x00000000, 37, // 166
            0x621B97C3, 0x00000000, 38, // 167
            0xC30C30C3, 0xC30C30C3, 39, // 168
            0x60F25DEB, 0x00000000, 38, // 169
            0xC0C0C0C1, 0x00000000, 39, // 170
            0x17F405FD, 0x17F405FD, 36, // 171
            0x2FA0BE83, 0x00000000, 37, // 172
            0xBD691047, 0xBD691047, 39, // 173
            0x2F149903, 0x00000000, 37, // 174
            0x5D9F7391, 0x00000000, 38, // 175
            0xBA2E8BA3, 0x00000000, 39, // 176
            0x5C90A1FD, 0x5C90A1FD, 38, // 177
            0xB81702E1, 0x00000000, 39, // 178
            0x5B87DDAD, 0x5B87DDAD, 38, // 179
            0x5B05B05B, 0x5B05B05B, 38, // 180
            0xB509E68B, 0x00000000, 39, // 181
            0x2D02D02D, 0x2D02D02D, 37, // 182
            0xB30F6353, 0x00000000, 39, // 183
            0xB21642C9, 0x00000000, 39, // 184
            0x1623FA77, 0x1623FA77, 36, // 185
            0xB02C0B03, 0x00000000, 39, // 186
            0xAF3ADDC7, 0x00000000, 39, // 187
            0xAE4C415D, 0x00000000, 39, // 188
            0x15AC056B, 0x15AC056B, 36, // 189
            0x2B1DA461, 0x2B1DA461, 37, // 190
            0xAB8F69E3, 0x00000000, 39, // 191
            0xAAAAAAAB, 0x00000000, 39, // 192
            0x15390949, 0x00000000, 36, // 193
            0xA8E83F57, 0xA8E83F57, 39, // 194
            0x15015015, 0x15015015, 36, // 195
            0x5397829D, 0x00000000, 38, // 196
            0xA655C439, 0xA655C439, 39, // 197
            0xA57EB503, 0x00000000, 39, // 198
            0x5254E78F, 0x00000000, 38, // 199
            0x51EB851F, 0x00000000, 38, // 200
            0x028C1979, 0x00000000, 33, // 201
            0xA237C32B, 0xA237C32B, 39, // 202
            0xA16B312F, 0x00000000, 39, // 203
            0xA0A0A0A1, 0x00000000, 39, // 204
            0x4FEC04FF, 0x00000000, 38, // 205
            0x9F1165E7, 0x9F1165E7, 39, // 206
            0x27932B49, 0x00000000, 37, // 207
            0x4EC4EC4F, 0x00000000, 38, // 208
            0x9CC8E161, 0x00000000, 39, // 209
            0x27027027, 0x27027027, 37, // 210
            0x9B4C6F9F, 0x00000000, 39, // 211
            0x9A90E7D9, 0x9A90E7D9, 39, // 212
            0x99D722DB, 0x00000000, 39, // 213
            0x991F1A51, 0x991F1A51, 39, // 214
            0x4C346405, 0x00000000, 38, // 215
            0x97B425ED, 0x97B425ED, 39, // 216
            0x4B809701, 0x4B809701, 38, // 217
            0x2593F69B, 0x2593F69B, 37, // 218
            0x12B404AD, 0x12B404AD, 36, // 219
            0x94F2094F, 0x94F2094F, 39, // 220
            0x25116025, 0x25116025, 37, // 221
            0x24E6A171, 0x24E6A171, 37, // 222
            0x24BC44E1, 0x24BC44E1, 37, // 223
            0x49249249, 0x49249249, 38, // 224
            0x91A2B3C5, 0x00000000, 39, // 225
            0x90FDBC09, 0x90FDBC09, 39, // 226
            0x905A3863, 0x905A3863, 39, // 227
            0x47DC11F7, 0x47DC11F7, 38, // 228
            0x478BBCED, 0x00000000, 38, // 229
            0x8E78356D, 0x8E78356D, 39, // 230
            0x46ED2901, 0x46ED2901, 38, // 231
            0x8D3DCB09, 0x00000000, 39, // 232
            0x2328A701, 0x2328A701, 37, // 233
            0x23023023, 0x23023023, 37, // 234
            0x45B81A25, 0x45B81A25, 38, // 235
            0x22B63CBF, 0x00000000, 37, // 236
            0x08A42F87, 0x08A42F87, 35, // 237
            0x44D72045, 0x00000000, 38, // 238
            0x891AC73B, 0x00000000, 39, // 239
            0x88888889, 0x00000000, 39, // 240
            0x10FEF011, 0x00000000, 36, // 241
            0x8767AB5F, 0x8767AB5F, 39, // 242
            0x86D90545, 0x00000000, 39, // 243
            0x4325C53F, 0x00000000, 38, // 244
            0x85BF3761, 0x85BF3761, 39, // 245
            0x85340853, 0x85340853, 39, // 246
            0x10953F39, 0x10953F39, 36, // 247
            0x42108421, 0x42108421, 38, // 248
            0x41CC9829, 0x41CC9829, 38, // 249
            0x10624DD3, 0x00000000, 36, // 250
            0x828CBFBF, 0x00000000, 39, // 251
            0x41041041, 0x41041041, 38, // 252
            0x81848DA9, 0x00000000, 39, // 253
            0x10204081, 0x10204081, 36, // 254
            0x80808081, 0x00000000, 39  // 255
        };

        DEFINE_STANDARD_OP(Normal, OVER)
        DEFINE_STANDARD_OP(Multiply, MULTIPLY)
        DEFINE_STANDARD_OP(Additive, SATURATE)
        DEFINE_STANDARD_OP(ColorBurn, COLORBURN)
        DEFINE_STANDARD_OP(ColorDodge, COLORDODGE)
        DEFINE_STANDARD_OP(Reflect, REFLECT)
        DEFINE_STANDARD_OP(Glow, GLOW)
        DEFINE_STANDARD_OP(Overlay, OVERLAY)
        DEFINE_STANDARD_OP(Difference, DIFFERENCE)
        DEFINE_STANDARD_OP(Negation, NEGATION)
        DEFINE_STANDARD_OP(Lighten, MAX)
        DEFINE_STANDARD_OP(Darken, MIN)
        DEFINE_STANDARD_OP(Screen, SCREEN)
        DEFINE_STANDARD_OP(Xor, XOR)
    }
}
