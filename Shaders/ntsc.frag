#version 330 core
out vec4 FragColor;
in vec2 TexCoord;

uniform sampler2D screenTexture;
uniform vec2 resolution;
uniform float time;

uniform int gammaMode; // 0=None, 1=Standard, 2=P22, 3=Measured, 4=SMPTE, 5=CrtProper
uniform float crtLw;
uniform float crtDb;

// Reference: GammaUtils.cs
float LinearTosRGB(float val)
{
    if (val <= 0.00304) return val * 12.92;
    return 1.055 * pow(val, 1.0 / 2.4) - 0.055;
}

float CrtProperToLinear(float val, float lw, float db)
{
    float alpha1 = 2.6;
    float alpha2 = 3.0;
    float vc = 0.35;
    float k = lw / pow(1.0 + db, alpha1);
    
    if (val < vc)
    {
        return k * pow(vc + db, alpha1 - alpha2) * pow(val + db, alpha2);
    }
    return k * pow(val + db, alpha1);
}

float CrtProper2ToLinear(float val)
{
    float Alpha = 0.111572;
    float Beta = 1.111572;
    float Cut = 0.091286;
    
    if (val >= 0.36) return pow(val, 2.31);
    
    float dFrac = val / 0.36;
    float part1 = (val <= Cut) ? (val / 4.0) : pow((val + Alpha) / Beta, 1.0 / 0.45);
    return (part1 * (1.0 - dFrac)) + (dFrac * pow(val, 2.31));
}

float SMPTE240MtoLinear(float val)
{
    float Alpha = 0.111572;
    float Beta = 1.111572;
    if (val < 0.0913) return val / 4.0;
    return pow((val + Alpha) / Beta, 1.0 / 0.45);
}

// NTSC-like shader
// Simulates scanlines, horizontal blur, and basic color bleeding

void main()
{ 
    vec2 uv = TexCoord;
    vec2 oneX = vec2(1.0 / resolution.x, 0.0);
    
    // YIQ / RGB Matrices
    // RGB to YIQ
    const vec3 kY = vec3(0.299, 0.587, 0.114);
    const vec3 kI = vec3(0.596, -0.274, -0.322);
    const vec3 kQ = vec3(0.211, -0.523, 0.312);

    // 1. & 3. Bandwidth extraction (Luma) & Bleed (Chroma)
    // Sample a window for Chroma (Low Bandwidth)
    // Sample center for Luma (High Bandwidth)
    
	vec3 centerRGB = texture(screenTexture, uv).rgb;
    float y = dot(centerRGB, kY);
    
    float iSum = 0.0;
    float qSum = 0.0;
    float weightSum = 0.0;
    
    // NTSC color carrier is ~3.58MHz, bandwidth allows for ~1/3 resolution of luma.
    // We smear chroma over a few pixels.
    float bleed = 2.0; 
    for(float x = -bleed; x <= bleed; x += 1.0)
    {
        vec3 rgb = texture(screenTexture, uv + oneX * x).rgb;
        float i = dot(rgb, kI);
        float q = dot(rgb, kQ);
        
        // Gaussianish weight
        float w = 1.0 / (1.0 + abs(x));
        
        iSum += i * w;
        qSum += q * w;
        weightSum += w;
    }
    
    float finalI = iSum / weightSum;
    float finalQ = qSum / weightSum;
    
    // Convert back to RGB
    // R = Y + 0.956I + 0.621Q
    // G = Y - 0.272I - 0.647Q
    // B = Y - 1.106I + 1.703Q
    
    vec3 color = vec3(
        y + 0.956 * finalI + 0.621 * finalQ,
        y - 0.272 * finalI - 0.647 * finalQ,
        y - 1.106 * finalI + 1.703 * finalQ
    );

    // 2. Scanlines
    // Based on UV.y. 240 lines.
    float scanline = sin(uv.y * 240.0 * 3.14159 * 2.0);
    color *= (0.95 + 0.05 * scanline);
    
    // 4. Gamma
    // Input is usually linear or sRGB. Output to sRGB.
    // Assuming input acts as sRGB for now.
    
    // Apply Gamma Correction based on mode
    // Signal -> Linear -> sRGB
    
    if (gammaMode > 0)
    {
        vec3 linColor = color;
        
        // 1. Signal to Linear
        if (gammaMode == 1) { /* Standard: Input is Linear */ }
        else if (gammaMode == 2) { linColor = pow(color, vec3(2.2)); }
        else if (gammaMode == 3) { 
            linColor.r = CrtProper2ToLinear(color.r);
            linColor.g = CrtProper2ToLinear(color.g);
            linColor.b = CrtProper2ToLinear(color.b);
        }
        else if (gammaMode == 4) {
            linColor.r = SMPTE240MtoLinear(color.r);
            linColor.g = SMPTE240MtoLinear(color.g);
            linColor.b = SMPTE240MtoLinear(color.b);
        }
        else if (gammaMode == 5) {
            linColor.r = CrtProperToLinear(color.r, crtLw, crtDb);
            linColor.g = CrtProperToLinear(color.g, crtLw, crtDb);
            linColor.b = CrtProperToLinear(color.b, crtLw, crtDb);
        }
        
        // 2. Linear to sRGB - Omitted for backend sRGB handling
        // color.r = LinearTosRGB(linColor.r);
        // color.g = LinearTosRGB(linColor.g);
        // color.b = LinearTosRGB(linColor.b);
        color = linColor;
    }
    
    FragColor = vec4(color, 1.0);
}
