using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// TDV Character Set implementation supporting all 9 TDV character sets
    /// </summary>
    public static class TDVCharacterSets
    {
        #region Character Set Definitions

        /// <summary>
        /// TDV Character Set enumeration
        /// </summary>
        public enum TDVCharacterSetType
        {
            /// <summary>
            /// US ASCII - Standard 7-bit ASCII (default character set)
            /// </summary>
            USASCII = 0,

            /// <summary>
            /// Graphics I - For TDV2115: Block graphics and line drawing.
            /// For TDV2200: Greek alphabet, mathematical characters, graphic symbols (ESC A).
            /// NOTE: TDV2200 has different character sets than TDV2115!
            /// </summary>
            GraphicsI = 1,

            /// <summary>
            /// Graphics II - For TDV2115: Extended graphics.
            /// For TDV2200: Subscripts, superscripts, special characters (ESC 0).
            /// NOTE: TDV2200 has different character sets than TDV2115!
            /// </summary>
            GraphicsII = 2,

            /// <summary>
            /// Math - Mathematical symbols
            /// </summary>
            Math = 3,

            /// <summary>
            /// Greek - Greek letters
            /// </summary>
            Greek = 4,

            /// <summary>
            /// Diacritics - Accented characters
            /// </summary>
            Diacritics = 5,

            /// <summary>
            /// Box - Box drawing characters
            /// </summary>
            Box = 6,

            /// <summary>
            /// NIX - Norwegian/Danish characters
            /// </summary>
            NIX = 7,

            /// <summary>
            /// T - Technical symbols
            /// </summary>
            T = 8,

            /// <summary>
            /// ND - ND private character set
            /// </summary>
            ND = 9
        }

        #endregion

        #region Character Set Mapping Tables

        /// <summary>
        /// Graphics I character set (ESC 1)
        /// Block graphics and line drawing characters
        /// </summary>
        private static readonly Dictionary<char, char> GraphicsI = new Dictionary<char, char>
        {
            // Block graphics
            { (char)0x60, '◆' }, // grave accent -> diamond
            { (char)0x61, '▒' }, // a -> medium shade
            { (char)0x62, '▓' }, // b -> dark shade
            { (char)0x63, '│' }, // c -> box drawing light vertical
            { (char)0x64, '┤' }, // d -> box drawing light vertical and left
            { (char)0x65, '╡' }, // e -> box drawing light left
            { (char)0x66, '╢' }, // f -> box drawing light right
            { (char)0x67, '╖' }, // g -> box drawing light down and left
            { (char)0x68, '╕' }, // h -> box drawing light down and right
            { (char)0x69, '╣' }, // i -> box drawing light vertical and right
            { (char)0x6A, '║' }, // j -> box drawing light vertical
            { (char)0x6B, '╗' }, // k -> box drawing light up and left
            { (char)0x6C, '╝' }, // l -> box drawing light up and right
            { (char)0x6D, '╜' }, // m -> box drawing light up
            { (char)0x6E, '╛' }, // n -> box drawing light down
            { (char)0x6F, '┐' }, // o -> box drawing light down and left
            { (char)0x70, '└' }, // p -> box drawing light up and right
            { (char)0x71, '┴' }, // q -> box drawing light up and horizontal
            { (char)0x72, '┬' }, // r -> box drawing light down and horizontal
            { (char)0x73, '├' }, // s -> box drawing light vertical and right
            { (char)0x74, '─' }, // t -> box drawing light horizontal
            { (char)0x75, '┼' }, // u -> box drawing light vertical and horizontal
            { (char)0x76, '╞' }, // v -> box drawing light vertical
            { (char)0x77, '╟' }, // w -> box drawing light vertical
            { (char)0x78, '╚' }, // x -> box drawing light up and right
            { (char)0x79, '╔' }, // y -> box drawing light down and right
            { (char)0x7A, '╩' }, // z -> box drawing light up and horizontal
            { (char)0x7B, '╦' }, // { -> box drawing light down and horizontal
            { (char)0x7C, '╠' }, // | -> box drawing light vertical and right
            { (char)0x7D, '═' }, // } -> box drawing light horizontal
            { (char)0x7E, '╬' }, // ~ -> box drawing light vertical and horizontal
            { (char)0x7F, '▀' }  // DEL -> upper half block
        };

        /// <summary>
        /// Graphics II character set (ESC 2)
        /// Extended graphics characters
        /// </summary>
        private static readonly Dictionary<char, char> GraphicsII = new Dictionary<char, char>
        {
            // Extended graphics
            { (char)0x60, '●' }, // grave accent -> bullet
            { (char)0x61, '○' }, // a -> white circle
            { (char)0x62, '◎' }, // b -> bullseye
            { (char)0x63, '◐' }, // c -> circle with left half black
            { (char)0x64, '◑' }, // d -> circle with right half black
            { (char)0x65, '◒' }, // e -> circle with lower half black
            { (char)0x66, '◓' }, // f -> circle with upper half black
            { (char)0x67, '■' }, // g -> black square
            { (char)0x68, '□' }, // h -> white square
            { (char)0x69, '▪' }, // i -> black small square
            { (char)0x6A, '▫' }, // j -> white small square
            { (char)0x6B, '▲' }, // k -> black up-pointing triangle
            { (char)0x6C, '△' }, // l -> white up-pointing triangle
            { (char)0x6D, '▼' }, // m -> black down-pointing triangle
            { (char)0x6E, '▽' }, // n -> white down-pointing triangle
            { (char)0x6F, '◀' }, // o -> black left-pointing triangle
            { (char)0x70, '◁' }, // p -> white left-pointing triangle
            { (char)0x71, '▶' }, // q -> black right-pointing triangle
            { (char)0x72, '▷' }, // r -> white right-pointing triangle
            { (char)0x73, '◆' }, // s -> black diamond
            { (char)0x74, '◇' }, // t -> white diamond
            { (char)0x75, '◊' }, // u -> lozenge
            { (char)0x76, '◈' }, // v -> white diamond containing black small diamond
            { (char)0x77, '◘' }, // w -> inverse bullet
            { (char)0x78, '◙' }, // x -> inverse white circle
            { (char)0x79, '◌' }, // y -> dotted circle
            { (char)0x7A, '◍' }, // z -> circle with vertical fill
            { (char)0x7B, '◎' }, // { -> bullseye
            { (char)0x7C, '◐' }, // | -> circle with left half black
            { (char)0x7D, '◑' }, // } -> circle with right half black
            { (char)0x7E, '◒' }, // ~ -> circle with lower half black
            { (char)0x7F, '◓' }  // DEL -> circle with upper half black
        };

        /// <summary>
        /// Math character set (ESC 3)
        /// Mathematical symbols
        /// </summary>
        private static readonly Dictionary<char, char> Math = new Dictionary<char, char>
        {
            { (char)0x60, '∀' }, // grave accent -> for all
            { (char)0x61, '∂' }, // a -> partial differential
            { (char)0x62, '∃' }, // b -> there exists
            { (char)0x63, '∅' }, // c -> empty set
            { (char)0x64, '∇' }, // d -> nabla
            { (char)0x65, '∈' }, // e -> element of
            { (char)0x66, '∉' }, // f -> not an element of
            { (char)0x67, '∋' }, // g -> contains as member
            { (char)0x68, '∌' }, // h -> does not contain as member
            { (char)0x69, '∏' }, // i -> n-ary product
            { (char)0x6A, '∑' }, // j -> n-ary summation
            { (char)0x6B, '−' }, // k -> minus sign
            { (char)0x6C, '∗' }, // l -> asterisk operator
            { (char)0x6D, '√' }, // m -> square root
            { (char)0x6E, '∝' }, // n -> proportional to
            { (char)0x6F, '∞' }, // o -> infinity
            { (char)0x70, '∠' }, // p -> angle
            { (char)0x71, '∧' }, // q -> logical and
            { (char)0x72, '∨' }, // r -> logical or
            { (char)0x73, '∩' }, // s -> intersection
            { (char)0x74, '∪' }, // t -> union
            { (char)0x75, '∫' }, // u -> integral
            { (char)0x76, '∴' }, // v -> therefore
            { (char)0x77, '∵' }, // w -> because
            { (char)0x78, '∼' }, // x -> tilde operator
            { (char)0x79, '≃' }, // y -> asymptotically equal to
            { (char)0x7A, '≈' }, // z -> almost equal to
            { (char)0x7B, '≡' }, // { -> identical to
            { (char)0x7C, '≤' }, // | -> less-than or equal to
            { (char)0x7D, '≥' }, // } -> greater-than or equal to
            { (char)0x7E, '≠' }, // ~ -> not equal to
            { (char)0x7F, '≅' }  // DEL -> approximately equal to
        };

        /// <summary>
        /// Greek character set (ESC 4)
        /// Greek letters
        /// </summary>
        private static readonly Dictionary<char, char> Greek = new Dictionary<char, char>
        {
            { (char)0x60, 'Α' }, // grave accent -> Alpha
            { (char)0x61, 'Β' }, // a -> Beta
            { (char)0x62, 'Γ' }, // b -> Gamma
            { (char)0x63, 'Δ' }, // c -> Delta
            { (char)0x64, 'Ε' }, // d -> Epsilon
            { (char)0x65, 'Ζ' }, // e -> Zeta
            { (char)0x66, 'Η' }, // f -> Eta
            { (char)0x67, 'Θ' }, // g -> Theta
            { (char)0x68, 'Ι' }, // h -> Iota
            { (char)0x69, 'Κ' }, // i -> Kappa
            { (char)0x6A, 'Λ' }, // j -> Lambda
            { (char)0x6B, 'Μ' }, // k -> Mu
            { (char)0x6C, 'Ν' }, // l -> Nu
            { (char)0x6D, 'Ξ' }, // m -> Xi
            { (char)0x6E, 'Ο' }, // n -> Omicron
            { (char)0x6F, 'Π' }, // o -> Pi
            { (char)0x70, 'Ρ' }, // p -> Rho
            { (char)0x71, 'Σ' }, // q -> Sigma
            { (char)0x72, 'Τ' }, // r -> Tau
            { (char)0x73, 'Υ' }, // s -> Upsilon
            { (char)0x74, 'Φ' }, // t -> Phi
            { (char)0x75, 'Χ' }, // u -> Chi
            { (char)0x76, 'Ψ' }, // v -> Psi
            { (char)0x77, 'Ω' }, // w -> Omega
            { (char)0x78, 'α' }, // x -> alpha
            { (char)0x79, 'β' }, // y -> beta
            { (char)0x7A, 'γ' }, // z -> gamma
            { (char)0x7B, 'δ' }, // { -> delta
            { (char)0x7C, 'ε' }, // | -> epsilon
            { (char)0x7D, 'ζ' }, // } -> zeta
            { (char)0x7E, 'η' }, // ~ -> eta
            { (char)0x7F, 'θ' }  // DEL -> theta
        };

        /// <summary>
        /// Diacritics character set (ESC 5)
        /// Accented characters
        /// </summary>
        private static readonly Dictionary<char, char> Diacritics = new Dictionary<char, char>
        {
            { (char)0x60, 'À' }, // grave accent -> A with grave
            { (char)0x61, 'Á' }, // a -> A with acute
            { (char)0x62, 'Â' }, // b -> A with circumflex
            { (char)0x63, 'Ã' }, // c -> A with tilde
            { (char)0x64, 'Ä' }, // d -> A with diaeresis
            { (char)0x65, 'Å' }, // e -> A with ring above
            { (char)0x66, 'Æ' }, // f -> AE
            { (char)0x67, 'Ç' }, // g -> C with cedilla
            { (char)0x68, 'È' }, // h -> E with grave
            { (char)0x69, 'É' }, // i -> E with acute
            { (char)0x6A, 'Ê' }, // j -> E with circumflex
            { (char)0x6B, 'Ë' }, // k -> E with diaeresis
            { (char)0x6C, 'Ì' }, // l -> I with grave
            { (char)0x6D, 'Í' }, // m -> I with acute
            { (char)0x6E, 'Î' }, // n -> I with circumflex
            { (char)0x6F, 'Ï' }, // o -> I with diaeresis
            { (char)0x70, 'Ð' }, // p -> Eth
            { (char)0x71, 'Ñ' }, // q -> N with tilde
            { (char)0x72, 'Ò' }, // r -> O with grave
            { (char)0x73, 'Ó' }, // s -> O with acute
            { (char)0x74, 'Ô' }, // t -> O with circumflex
            { (char)0x75, 'Õ' }, // u -> O with tilde
            { (char)0x76, 'Ö' }, // v -> O with diaeresis
            { (char)0x77, 'Ø' }, // w -> O with stroke
            { (char)0x78, 'Ù' }, // x -> U with grave
            { (char)0x79, 'Ú' }, // y -> U with acute
            { (char)0x7A, 'Û' }, // z -> U with circumflex
            { (char)0x7B, 'Ü' }, // { -> U with diaeresis
            { (char)0x7C, 'Ý' }, // | -> Y with acute
            { (char)0x7D, 'Þ' }, // } -> Thorn
            { (char)0x7E, 'ß' }, // ~ -> sharp s
            { (char)0x7F, 'à' }  // DEL -> a with grave
        };

        /// <summary>
        /// Box character set (ESC 6)
        /// Box drawing characters
        /// </summary>
        private static readonly Dictionary<char, char> Box = new Dictionary<char, char>
        {
            { (char)0x60, '┌' }, // grave accent -> box drawing light down and right
            { (char)0x61, '┐' }, // a -> box drawing light down and left
            { (char)0x62, '└' }, // b -> box drawing light up and right
            { (char)0x63, '┘' }, // c -> box drawing light up and left
            { (char)0x64, '├' }, // d -> box drawing light vertical and right
            { (char)0x65, '┤' }, // e -> box drawing light vertical and left
            { (char)0x66, '┬' }, // f -> box drawing light down and horizontal
            { (char)0x67, '┴' }, // g -> box drawing light up and horizontal
            { (char)0x68, '┼' }, // h -> box drawing light vertical and horizontal
            { (char)0x69, '─' }, // i -> box drawing light horizontal
            { (char)0x6A, '│' }, // j -> box drawing light vertical
            { (char)0x6B, '┏' }, // k -> box drawing heavy down and right
            { (char)0x6C, '┓' }, // l -> box drawing heavy down and left
            { (char)0x6D, '┗' }, // m -> box drawing heavy up and right
            { (char)0x6E, '┛' }, // n -> box drawing heavy up and left
            { (char)0x6F, '┣' }, // o -> box drawing heavy vertical and right
            { (char)0x70, '┫' }, // p -> box drawing heavy vertical and left
            { (char)0x71, '┳' }, // q -> box drawing heavy down and horizontal
            { (char)0x72, '┻' }, // r -> box drawing heavy up and horizontal
            { (char)0x73, '╋' }, // s -> box drawing heavy vertical and horizontal
            { (char)0x74, '━' }, // t -> box drawing heavy horizontal
            { (char)0x75, '┃' }, // u -> box drawing heavy vertical
            { (char)0x76, '╔' }, // v -> box drawing double down and right
            { (char)0x77, '╗' }, // w -> box drawing double down and left
            { (char)0x78, '╚' }, // x -> box drawing double up and right
            { (char)0x79, '╝' }, // y -> box drawing double up and left
            { (char)0x7A, '╠' }, // z -> box drawing double vertical and right
            { (char)0x7B, '╣' }, // { -> box drawing double vertical and left
            { (char)0x7C, '╦' }, // | -> box drawing double down and horizontal
            { (char)0x7D, '╩' }, // } -> box drawing double up and horizontal
            { (char)0x7E, '╬' }, // ~ -> box drawing double vertical and horizontal
            { (char)0x7F, '═' }  // DEL -> box drawing double horizontal
        };

        /// <summary>
        /// NIX character set (ESC 7)
        /// Norwegian/Danish characters
        /// </summary>
        private static readonly Dictionary<char, char> NIX = new Dictionary<char, char>
        {
            { (char)0x60, 'Æ' }, // grave accent -> AE
            { (char)0x61, 'Ø' }, // a -> O with stroke
            { (char)0x62, 'Å' }, // b -> A with ring above
            { (char)0x63, 'æ' }, // c -> ae
            { (char)0x64, 'ø' }, // d -> o with stroke
            { (char)0x65, 'å' }, // e -> a with ring above
            { (char)0x66, 'Ä' }, // f -> A with diaeresis
            { (char)0x67, 'Ö' }, // g -> O with diaeresis
            { (char)0x68, 'Ü' }, // h -> U with diaeresis
            { (char)0x69, 'ä' }, // i -> a with diaeresis
            { (char)0x6A, 'ö' }, // j -> o with diaeresis
            { (char)0x6B, 'ü' }, // k -> u with diaeresis
            { (char)0x6C, 'É' }, // l -> E with acute
            { (char)0x6D, 'é' }, // m -> e with acute
            { (char)0x6E, 'È' }, // n -> E with grave
            { (char)0x6F, 'è' }, // o -> e with grave
            { (char)0x70, 'Ê' }, // p -> E with circumflex
            { (char)0x71, 'ê' }, // q -> e with circumflex
            { (char)0x72, 'Ë' }, // r -> E with diaeresis
            { (char)0x73, 'ë' }, // s -> e with diaeresis
            { (char)0x74, 'À' }, // t -> A with grave
            { (char)0x75, 'à' }, // u -> a with grave
            { (char)0x76, 'Á' }, // v -> A with acute
            { (char)0x77, 'á' }, // w -> a with acute
            { (char)0x78, 'Â' }, // x -> A with circumflex
            { (char)0x79, 'â' }, // y -> a with circumflex
            { (char)0x7A, 'Ã' }, // z -> A with tilde
            { (char)0x7B, 'ã' }, // { -> a with tilde
            { (char)0x7C, 'Ç' }, // | -> C with cedilla
            { (char)0x7D, 'ç' }, // } -> c with cedilla
            { (char)0x7E, 'Ñ' }, // ~ -> N with tilde
            { (char)0x7F, 'ñ' }  // DEL -> n with tilde
        };

        /// <summary>
        /// T character set (ESC 8)
        /// Technical symbols
        /// </summary>
        private static readonly Dictionary<char, char> T = new Dictionary<char, char>
        {
            { (char)0x60, '°' }, // grave accent -> degree sign
            { (char)0x61, '±' }, // a -> plus-minus sign
            { (char)0x62, '²' }, // b -> superscript two
            { (char)0x63, '³' }, // c -> superscript three
            { (char)0x64, '×' }, // d -> multiplication sign
            { (char)0x65, '÷' }, // e -> division sign
            { (char)0x66, '¼' }, // fol -> vulgar fraction one quarter
            { (char)0x67, '½' }, // g -> vulgar fraction one half
            { (char)0x68, '¾' }, // h -> vulgar fraction three quarters
            { (char)0x69, '§' }, // i -> section sign
            { (char)0x6A, '¶' }, // j -> pilcrow sign
            { (char)0x6B, '†' }, // k -> dagger
            { (char)0x6C, '‡' }, // l -> double dagger
            { (char)0x6D, '•' }, // m -> bullet
            { (char)0x6E, '…' }, // n -> horizontal ellipsis
            { (char)0x6F, '‰' }, // o -> per mille sign
            { (char)0x70, '′' }, // p -> prime
            { (char)0x71, '″' }, // q -> double prime
            { (char)0x72, '‴' }, // r -> triple prime
            { (char)0x73, '‵' }, // s -> reversed prime
            { (char)0x74, '‶' }, // t -> reversed double prime
            { (char)0x75, '‷' }, // u -> reversed triple prime
            { (char)0x76, '‸' }, // v -> caret
            { (char)0x77, '‹' }, // w -> single left-pointing angle quotation mark
            { (char)0x78, '›' }, // x -> single right-pointing angle quotation mark
            { (char)0x79, '※' }, // y -> reference mark
            { (char)0x7A, '‽' }, // z -> interrobang
            { (char)0x7B, '‾' }, // { -> overline
            { (char)0x7C, '‿' }, // | -> undertie
            { (char)0x7D, '⁀' }, // } -> character tie
            { (char)0x7E, '⁁' }, // ~ -> caret insertion point
            { (char)0x7F, '⁂' }  // DEL -> asterism
        };

        /// <summary>
        /// ND character set (ESC 9)
        /// ND private character set
        /// </summary>
        private static readonly Dictionary<char, char> ND = new Dictionary<char, char>
        {
            { (char)0x60, '◊' }, // grave accent -> lozenge
            { (char)0x61, '◆' }, // a -> black diamond
            { (char)0x62, '◇' }, // b -> white diamond
            { (char)0x63, '◈' }, // c -> white diamond containing black small diamond
            { (char)0x64, '◘' }, // d -> inverse bullet
            { (char)0x65, '◙' }, // e -> inverse white circle
            { (char)0x66, '◌' }, // f -> dotted circle
            { (char)0x67, '◍' }, // g -> circle with vertical fill
            { (char)0x68, '◎' }, // h -> bullseye
            { (char)0x69, '◐' }, // i -> circle with left half black
            { (char)0x6A, '◑' }, // j -> circle with right half black
            { (char)0x6B, '◒' }, // k -> circle with lower half black
            { (char)0x6C, '◓' }, // l -> circle with upper half black
            { (char)0x6D, '●' }, // m -> bullet
            { (char)0x6E, '○' }, // n -> white circle
            { (char)0x6F, '■' }, // o -> black square
            { (char)0x70, '□' }, // p -> white square
            { (char)0x71, '▪' }, // q -> black small square
            { (char)0x72, '▫' }, // r -> white small square
            { (char)0x73, '▲' }, // s -> black up-pointing triangle
            { (char)0x74, '△' }, // t -> white up-pointing triangle
            { (char)0x75, '▼' }, // u -> black down-pointing triangle
            { (char)0x76, '▽' }, // v -> white down-pointing triangle
            { (char)0x77, '◀' }, // w -> black left-pointing triangle
            { (char)0x78, '◁' }, // x -> white left-pointing triangle
            { (char)0x79, '▶' }, // y -> black right-pointing triangle
            { (char)0x7A, '▷' }, // z -> white right-pointing triangle
            { (char)0x7B, '◄' }, // { -> black left-pointing small triangle
            { (char)0x7C, '◅' }, // | -> white left-pointing small triangle
            { (char)0x7D, '►' }, // } -> black right-pointing small triangle
            { (char)0x7E, '▻' }, // ~ -> white right-pointing small triangle
            { (char)0x7F, '◄' }  // DEL -> black left-pointing small triangle
        };

        #endregion

        #region ISO 646 National Variant Tables

        /// <summary>
        /// ISO 646 Norwegian/Danish variant (IRV replacements)
        /// Maps ASCII positions to Norwegian characters
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Norwegian = new Dictionary<char, char>
        {
            { '@', 'Ä' },   // 0x40
            { '[', 'Æ' },   // 0x5B
            { '\\', 'Ø' },  // 0x5C
            { ']', 'Å' },   // 0x5D
            { '^', 'Ü' },   // 0x5E
            { '`', 'ä' },   // 0x60
            { '{', 'æ' },   // 0x7B
            { '|', 'ø' },   // 0x7C
            { '}', 'å' },   // 0x7D
            { '~', 'ü' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 Swedish variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Swedish = new Dictionary<char, char>
        {
            { '@', 'É' },   // 0x40
            { '[', 'Ä' },   // 0x5B
            { '\\', 'Ö' },  // 0x5C
            { ']', 'Å' },   // 0x5D
            { '^', 'Ü' },   // 0x5E
            { '`', 'é' },   // 0x60
            { '{', 'ä' },   // 0x7B
            { '|', 'ö' },   // 0x7C
            { '}', 'å' },   // 0x7D
            { '~', 'ü' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 Danish variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Danish = new Dictionary<char, char>
        {
            { '@', 'Ä' },   // 0x40
            { '[', 'Æ' },   // 0x5B
            { '\\', 'Ø' },  // 0x5C
            { ']', 'Å' },   // 0x5D
            { '^', 'Ü' },   // 0x5E
            { '`', 'ä' },   // 0x60
            { '{', 'æ' },   // 0x7B
            { '|', 'ø' },   // 0x7C
            { '}', 'å' },   // 0x7D
            { '~', 'ü' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 Finnish variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Finnish = new Dictionary<char, char>
        {
            { '[', 'Ä' },   // 0x5B
            { '\\', 'Ö' },  // 0x5C
            { ']', 'Å' },   // 0x5D
            { '^', 'Ü' },   // 0x5E
            { '`', 'é' },   // 0x60
            { '{', 'ä' },   // 0x7B
            { '|', 'ö' },   // 0x7C
            { '}', 'å' },   // 0x7D
            { '~', 'ü' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 German variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646German = new Dictionary<char, char>
        {
            { '@', '§' },   // 0x40
            { '[', 'Ä' },   // 0x5B
            { '\\', 'Ö' },  // 0x5C
            { ']', 'Ü' },   // 0x5D
            { '^', '^' },   // 0x5E (unchanged)
            { '`', '`' },   // 0x60 (unchanged)
            { '{', 'ä' },   // 0x7B
            { '|', 'ö' },   // 0x7C
            { '}', 'ü' },   // 0x7D
            { '~', 'ß' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 UK variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646UK = new Dictionary<char, char>
        {
            { '#', '£' }    // 0x23 -> pound sign
        };

        /// <summary>
        /// ISO 646 French variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646French = new Dictionary<char, char>
        {
            { '#', '£' },   // 0x23
            { '@', 'à' },   // 0x40
            { '[', '°' },   // 0x5B
            { '\\', 'ç' },  // 0x5C
            { ']', '§' },   // 0x5D
            { '{', 'é' },   // 0x7B
            { '|', 'ù' },   // 0x7C
            { '}', 'è' },   // 0x7D
            { '~', '¨' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 Italian variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Italian = new Dictionary<char, char>
        {
            { '#', '£' },   // 0x23
            { '@', '§' },   // 0x40
            { '[', '°' },   // 0x5B
            { '\\', 'ç' },  // 0x5C
            { ']', 'é' },   // 0x5D
            { '`', 'ù' },   // 0x60
            { '{', 'à' },   // 0x7B
            { '|', 'ò' },   // 0x7C
            { '}', 'è' },   // 0x7D
            { '~', 'ì' }    // 0x7E
        };

        /// <summary>
        /// ISO 646 Spanish variant
        /// </summary>
        private static readonly Dictionary<char, char> ISO646Spanish = new Dictionary<char, char>
        {
            { '#', '£' },   // 0x23
            { '@', '§' },   // 0x40
            { '[', '¡' },   // 0x5B
            { '\\', 'Ñ' },  // 0x5C
            { ']', '¿' },   // 0x5D
            { '{', '°' },   // 0x7B
            { '|', 'ñ' },   // 0x7C
            { '}', 'ç' },   // 0x7D
        };

        #endregion

        #region Character Set Lookup

        /// <summary>
        /// Get character set mapping by type
        /// </summary>
        /// <param name="characterSetType">
        /// The character set type
        /// </param>
        /// <returns>
        /// Character set mapping dictionary
        /// </returns>
        public static Dictionary<char, char> GetCharacterSet(TDVCharacterSetType characterSetType)
        {
            return characterSetType switch
            {
                TDVCharacterSetType.USASCII => new Dictionary<char, char>(),  // No mapping for US ASCII
                TDVCharacterSetType.GraphicsI => GraphicsI,
                TDVCharacterSetType.GraphicsII => GraphicsII,
                TDVCharacterSetType.Math => Math,
                TDVCharacterSetType.Greek => Greek,
                TDVCharacterSetType.Diacritics => Diacritics,
                TDVCharacterSetType.Box => Box,
                TDVCharacterSetType.NIX => NIX,
                TDVCharacterSetType.T => T,
                TDVCharacterSetType.ND => ND,
                _ => new Dictionary<char, char>()
            };
        }

        /// <summary>
        /// Get character from character set
        /// </summary>
        /// <param name="characterSetType">
        /// The character set type
        /// </param>
        /// <param name="inputChar">
        /// Input character
        /// </param>
        /// <returns>
        /// Mapped character or original if not found
        /// </returns>
        public static char GetCharacter(TDVCharacterSetType characterSetType, char inputChar)
        {
            var characterSet = GetCharacterSet(characterSetType);
            return characterSet.TryGetValue(inputChar, out char mappedChar) ? mappedChar : inputChar;
        }

        /// <summary>
        /// Check if character set exists
        /// </summary>
        /// <param name="characterSetType">
        /// The character set type
        /// </param>
        /// <returns>
        /// True if character set exists
        /// </returns>
        public static bool CharacterSetExists(TDVCharacterSetType characterSetType)
        {
            return characterSetType >= TDVCharacterSetType.GraphicsI && characterSetType <= TDVCharacterSetType.ND;
        }

        /// <summary>
        /// Get character set name
        /// </summary>
        /// <param name="characterSetType">
        /// The character set type
        /// </param>
        /// <returns>
        /// Character set name
        /// </returns>
        public static string GetCharacterSetName(TDVCharacterSetType characterSetType)
        {
            return characterSetType switch
            {
                TDVCharacterSetType.GraphicsI => "Graphics I",
                TDVCharacterSetType.GraphicsII => "Graphics II",
                TDVCharacterSetType.Math => "Math",
                TDVCharacterSetType.Greek => "Greek",
                TDVCharacterSetType.Diacritics => "Diacritics",
                TDVCharacterSetType.Box => "Box",
                TDVCharacterSetType.NIX => "NIX",
                TDVCharacterSetType.T => "T",
                TDVCharacterSetType.ND => "ND",
                _ => "Unknown"
            };
        }

        #endregion

        #region ISO 646 Variant Lookup

        /// <summary>
        /// Get ISO 646 variant character mapping
        /// </summary>
        /// <param name="variant">
        /// The ISO 646 variant
        /// </param>
        /// <returns>
        /// Character mapping dictionary for the variant
        /// </returns>
        public static Dictionary<char, char> GetISO646VariantMapping(TDV2200ISO646Variant variant)
        {
            return variant switch
            {
                TDV2200ISO646Variant.International => new Dictionary<char, char>(), // No mapping for International
                TDV2200ISO646Variant.Norwegian => ISO646Norwegian,
                TDV2200ISO646Variant.Swedish => ISO646Swedish,
                TDV2200ISO646Variant.German => ISO646German,
                _ => new Dictionary<char, char>()
            };
        }

        /// <summary>
        /// Apply ISO 646 variant character mapping
        /// </summary>
        /// <param name="variant">
        /// The ISO 646 variant
        /// </param>
        /// <param name="inputChar">
        /// Input character
        /// </param>
        /// <returns>
        /// Mapped character or original if not mapped
        /// </returns>
        public static char ApplyISO646Variant(TDV2200ISO646Variant variant, char inputChar)
        {
            if (variant == TDV2200ISO646Variant.International)
                return inputChar; // No mapping for International (US ASCII)

            var mapping = GetISO646VariantMapping(variant);
            return mapping.TryGetValue(inputChar, out char mappedChar) ? mappedChar : inputChar;
        }

        /// <summary>
        /// Check if a character is affected by ISO 646 variant mapping
        /// </summary>
        /// <param name="inputChar">
        /// Input character to check
        /// </param>
        /// <returns>
        /// True if the character may be remapped by ISO 646 variants
        /// </returns>
        public static bool IsISO646VariantPosition(char inputChar)
        {
            // ISO 646 variants typically remap these ASCII positions
            return inputChar == '#' || inputChar == '@' ||
                   inputChar == '[' || inputChar == '\\' || inputChar == ']' || inputChar == '^' ||
                   inputChar == '`' || inputChar == '{' || inputChar == '|' || inputChar == '}' || inputChar == '~';
        }

        /// <summary>
        /// Cached reverse ISO 646 maps, national character to ASCII wire byte, keyed by language.
        /// </summary>
        /// <remarks>
        /// CONCURRENT because it is genuinely reached from more than one thread. Text is converted
        /// when the user types on the UI thread, when the virtual keyboard sends, and when a script
        /// or MCP call sends from the session pump - so two threads can be here at once.
        ///
        /// It used to be a plain dictionary created on first use, with no lock: one thread could be
        /// writing the new entry while another read it, which corrupts a Dictionary rather than
        /// merely losing a value. It showed up as a test that passed alone and failed in the full
        /// run, which is the mild version of what that race can do.
        ///
        /// GetOrAdd may build the same map twice under contention. That is harmless - the maps are
        /// identical and immutable once built - and cheaper than locking every reader.
        /// </remarks>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<char, char>> _reverseISO646Cache = new();

        /// <summary>
        /// Get the forward ISO 646 mapping for a language code.
        /// Returns the ASCII→national character dictionary.
        /// </summary>
        public static Dictionary<char, char>? GetISO646ForLanguage(string languageCode)
        {
            return languageCode switch
            {
                "no" => ISO646Norwegian,
                "dk" => ISO646Danish,
                "sv" => ISO646Swedish,
                "de" => ISO646German,
                "fr" => ISO646French,
                "sds" => ISO646Norwegian,
                "en" => ISO646UK,
                "ch" => ISO646German,
                "fi" => ISO646Finnish,
                "us" => null,
                "fao" => null,
                "is" => null,
                _ => null
            };
        }

        /// <summary>
        /// Get the reverse ISO 646 mapping for a language code.
        /// Maps national display characters back to their ASCII wire byte values.
        /// E.g. for Norwegian: Ø → '\', ø → '|', Æ → '[', æ → '{', etc.
        /// Returns null if no mapping exists for this language (e.g. US ASCII).
        /// </summary>
        public static Dictionary<char, char>? GetReverseISO646ForLanguage(string languageCode)
        {
            if (_reverseISO646Cache.TryGetValue(languageCode, out var cached))
                return cached;

            var forward = GetISO646ForLanguage(languageCode);
            if (forward == null)
                return null;

            var reverse = new Dictionary<char, char>(forward.Count);
            // Build reverse: for each (asciiChar → nationalChar), store (nationalChar → asciiChar)
            // Skip identity mappings (e.g. German ^→^ and `→`)
            var enumerator = forward.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var asciiChar = enumerator.Current.Key;
                var nationalChar = enumerator.Current.Value;
                if (asciiChar != nationalChar)
                    reverse[nationalChar] = asciiChar;
            }

            // TryAdd rather than an indexed write: a second thread that built the same map at the
            // same time must not replace one another reader is already holding.
            _reverseISO646Cache.TryAdd(languageCode, reverse);
            return reverse;
        }

        /// <summary>
        /// Converts typed text into the bytes a TDV terminal expects on the wire under the given
        /// national variant.
        ///
        /// A TDV renders bytes through its active national replacement set, so typing 'ø' must put
        /// the ASCII position byte '|' on the wire, not a Unicode 'ø' — the terminal draws ø where
        /// it sees '|'. Same for Æ → '[', Å → ']', and the German and Swedish equivalents.
        ///
        /// WHY THIS IS HERE. The loop around the reverse table was written out three separate times
        /// in the UI — TerminalCanvas, VirtualKeyboardWindow and VirtualKeyboardPanel — and they had
        /// already drifted: the canvas converted a string of any length, while the window only
        /// converted input exactly one character long, so a multi-character paste or IME commit
        /// through that window went out unconverted. One implementation, one behaviour.
        ///
        /// Text passes through untouched when the variant is International (langCode null), which
        /// is the common case and needs no allocation.
        /// </summary>
        /// <param name="text">
        /// The text as typed.
        /// </param>
        /// <param name="languageCode">
        /// ISO 639 code of the active variant, or null for International.
        /// </param>
        public static string ConvertToWireBytes(string text, string? languageCode)
        {
            if (string.IsNullOrEmpty(text) || languageCode == null)
                return text;

            var reverseMap = GetReverseISO646ForLanguage(languageCode);
            if (reverseMap == null)
                return text;

            // Don't allocate for text that needs no conversion — most of it.
            bool needsConversion = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (reverseMap.ContainsKey(text[i]))
                {
                    needsConversion = true;
                    break;
                }
            }

            if (!needsConversion)
                return text;

            var result = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                result.Append(reverseMap.TryGetValue(text[i], out char wireByte) ? wireByte : text[i]);
            }

            return result.ToString();
        }

        /// <summary>
        /// ISO 639 code for a variant, or null for International (which needs no substitutions).
        /// TDVEmulatorBase.GetISO646LanguageCode delegates here so there is one answer, not two.
        /// </summary>
        public static string? GetLanguageCodeForVariant(TDV2200ISO646Variant variant)
        {
            return variant switch
            {
                TDV2200ISO646Variant.Norwegian => "no",
                TDV2200ISO646Variant.Swedish => "sv",
                TDV2200ISO646Variant.German => "de",
                _ => null
            };
        }

        /// <summary>
        /// Maps a Unicode national character back to the ASCII position its glyph occupies in the
        /// character-generator ROM under the given variant — Ø under Norwegian lives at '\' (0x5C).
        ///
        /// WHY THIS IS HERE. BitmapFontRenderer used to carry its own inline switch for this, and it
        /// was BOTH variant-blind and wrong: it mapped Æ→'^', Ø→'`', æ→'~' and ø→'`' — so Ø and ø
        /// resolved to the SAME position — where the ROM (and the ISO 646 tables above, and the
        /// FontTDV2215 header comment) put them at '[', '\', '{' and '|'. It also applied those
        /// substitutions no matter which variant was active, so a terminal in International mode
        /// would draw '[' for a Ä instead of drawing nothing.
        ///
        /// Returns false for plain ASCII, for International, and for characters the variant has no
        /// position for — in every one of those cases the caller should leave the character alone.
        /// </summary>
        public static bool TryMapUnicodeToRomPosition(char nationalChar, TDV2200ISO646Variant variant, out char romPosition)
        {
            romPosition = nationalChar;

            // Plain ASCII is already at its own position; nothing to resolve.
            if (nationalChar <= 0x7F)
                return false;

            string? languageCode = GetLanguageCodeForVariant(variant);
            if (languageCode == null)
                return false;

            var reverseMap = GetReverseISO646ForLanguage(languageCode);
            if (reverseMap == null)
                return false;

            return reverseMap.TryGetValue(nationalChar, out romPosition);
        }

        #endregion
    }
}
