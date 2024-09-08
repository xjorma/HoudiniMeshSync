void Clip_float(in float2 uv, out float alpha)
{
	float2 screenPosition = uv * _ScreenResolution;
	if (screenPosition.x <= _PanePosition.x || screenPosition.x > _PaneSize.x + _PanePosition.x || screenPosition.y <= _PanePosition.y || screenPosition.y > _PaneSize.y + _PanePosition.y)
		alpha = 1.0f;
	else
		alpha = 0.0f;
}