varying highp vec2 texCoord;

uniform sampler2D tex0;

void main()
{
    gl_FragColor = texture2D(tex0, texCoord);
}
