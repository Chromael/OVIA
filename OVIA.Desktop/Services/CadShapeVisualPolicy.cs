using System;

namespace OVIA.Desktop
{
    /// <summary>
    /// 철근형상 문자/라인의 표시 크기 정책입니다.
    /// CAD 원본 좌표/형상 데이터는 변경하지 않고 화면 표시와 ERP 전송 파생본에만 적용합니다.
    /// </summary>
    internal static class CadShapeVisualPolicy
    {
        public const double MinimumTextScale = 0.75D;
        public const double MaximumTextScale = 2.00D;

        public const float EditorBaseTextSizePt = 12F;
        public const float GridMinimumTextSizePt = 7F;
        public const float GridMaximumTextSizePt = 11.5F;

        public const double EditorCanonicalTextHeight = 2.5D;
        public const double ErpStandardTextHeight = 70D;
        public const double ErpTextWidthRatio = 0.092D;
        public const double ErpTextHeightRatio = 0.14D;

        public const float GridStrokeWidthPx = 1.25F;

        public static double ClampTextScale(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || value <= 0D)
            {
                return 1D;
            }

            return Math.Max(MinimumTextScale, Math.Min(MaximumTextScale, value));
        }

        public static float ClampGridFontSize(float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0F)
            {
                value = GridMinimumTextSizePt;
            }

            return Math.Max(GridMinimumTextSizePt, Math.Min(GridMaximumTextSizePt, value));
        }

        public static double ResolveErpReferenceTextHeight(double cellWidth, double cellHeight)
        {
            double widthReference = Math.Max(cellWidth, 0D) * ErpTextWidthRatio;
            double heightReference = Math.Max(cellHeight, 0D) * ErpTextHeightRatio;
            double reference = Math.Max(ErpStandardTextHeight, Math.Max(widthReference, heightReference));

            if (Double.IsNaN(reference) || Double.IsInfinity(reference) || reference <= 0D)
            {
                return ErpStandardTextHeight;
            }

            return reference;
        }
    }
}
