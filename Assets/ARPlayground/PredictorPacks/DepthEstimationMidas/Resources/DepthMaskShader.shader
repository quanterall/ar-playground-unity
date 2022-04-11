Shader "Custom/DepthMaskShader"
{
    Properties
	{
		_MainTex("_MainTex", 2D) = "white" {}
	}

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

		ZTest Always Cull Off ZWrite Off
		Fog { Mode off }

		Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            // properties
            float _MinDepth;
            float _MaxDepth;
			float _LogNormFactor;

            // model output texture
            sampler2D _MainTex;

            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

         
            v2f vert (appdata v)
            {
                v2f o;

                o.uv = float2(1 - v.uv.y, 1 - v.uv.x);  // newer versions of Barracuda mess up the X and Y directions
				o.vertex = UnityObjectToClipPos(v.vertex);
             
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
				float depth = tex2D(_MainTex, i.uv).x;

				float normDepth = saturate((depth - _MinDepth) / (_MaxDepth - _MinDepth));
				float logDepth = log(_LogNormFactor * (normDepth + 1)) / log(_LogNormFactor + 1);

				float3 col = logDepth;

				return float4(col, 1);
            }

            ENDCG
        }
    }
}
