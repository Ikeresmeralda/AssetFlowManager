package com.assetflow.manager.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat

/**
 * Paleta de AssetFlow Manager.
 *
 * Los valores son los mismos que los de `Theme/Tokens.xaml` en el cliente de
 * escritorio, copiados uno a uno. Es la razón de que estén escritos a mano en
 * lugar de usar el color dinámico de Material You: **las dos aplicaciones
 * tienen que verse como la misma**, y el color dinámico las pintaría de
 * distinto color en cada teléfono.
 */
object Paleta {
    val Canvas = Color(0xFFF6F7F9)
    val Surface = Color(0xFFFFFFFF)
    val SurfaceAlt = Color(0xFFFAFBFC)
    val SurfaceHover = Color(0xFFF1F3F6)
    val SurfaceSunken = Color(0xFFEDEFF3)

    val Nav = Color(0xFF171B22)
    val NavAlt = Color(0xFF1E232C)
    val NavActive = Color(0xFF2D3644)
    val NavText = Color(0xFFC3CAD6)
    val NavTextMuted = Color(0xFF78828F)

    val Border = Color(0xFFE2E5EA)
    val BorderStrong = Color(0xFFCDD2DA)

    val Text = Color(0xFF14171C)
    val TextMuted = Color(0xFF59626F)
    val TextSubtle = Color(0xFF7A8391)
    val TextDisabled = Color(0xFFA8AFB9)

    val Accent = Color(0xFF1F5FD6)
    val AccentHover = Color(0xFF1A52BC)
    val AccentSoft = Color(0xFFEBF1FC)
    val AccentSoftBorder = Color(0xFFC5D8F7)

    val Success = Color(0xFF15734A)
    val SuccessSoft = Color(0xFFE6F4EC)
    val SuccessBorder = Color(0xFFB4DEC7)

    val Warning = Color(0xFF8A5A00)
    val WarningSoft = Color(0xFFFDF3E0)
    val WarningBorder = Color(0xFFF0D9A8)

    val Danger = Color(0xFFB3271E)
    val DangerSoft = Color(0xFFFCEDEC)
    val DangerBorder = Color(0xFFF0BDB9)
}

private val EsquemaClaro = lightColorScheme(
    primary = Paleta.Accent,
    onPrimary = Color.White,
    primaryContainer = Paleta.AccentSoft,
    onPrimaryContainer = Paleta.Accent,
    secondary = Paleta.Nav,
    onSecondary = Color.White,
    background = Paleta.Canvas,
    onBackground = Paleta.Text,
    surface = Paleta.Surface,
    onSurface = Paleta.Text,
    surfaceVariant = Paleta.SurfaceSunken,
    onSurfaceVariant = Paleta.TextMuted,
    outline = Paleta.Border,
    outlineVariant = Paleta.BorderStrong,
    error = Paleta.Danger,
    onError = Color.White,
    errorContainer = Paleta.DangerSoft,
    onErrorContainer = Paleta.Danger
)

/**
 * Variante oscura.
 *
 * El cliente de escritorio no tiene modo oscuro, pero en un teléfono ignorar
 * el ajuste del sistema se nota mucho más. Se mantienen el acento y los
 * colores de estado para que las dos aplicaciones sigan reconociéndose.
 */
private val EsquemaOscuro = darkColorScheme(
    primary = Color(0xFF7BA7F0),
    onPrimary = Color(0xFF08214D),
    primaryContainer = Color(0xFF1B3A70),
    onPrimaryContainer = Color(0xFFD6E3FB),
    secondary = Paleta.NavText,
    onSecondary = Paleta.Nav,
    background = Color(0xFF12151A),
    onBackground = Color(0xFFE6E9EE),
    surface = Paleta.NavAlt,
    onSurface = Color(0xFFE6E9EE),
    surfaceVariant = Paleta.NavActive,
    onSurfaceVariant = Color(0xFFB6BECB),
    outline = Color(0xFF3A424F),
    outlineVariant = Color(0xFF2A313C),
    error = Color(0xFFF08B84),
    onError = Color(0xFF4A0E0A),
    errorContainer = Color(0xFF5C1712),
    onErrorContainer = Color(0xFFFAD5D2)
)

private val Tipografia = Typography(
    headlineMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.SemiBold,
        fontSize = 24.sp,
        lineHeight = 30.sp
    ),
    titleLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.SemiBold,
        fontSize = 19.sp,
        lineHeight = 25.sp
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.SemiBold,
        fontSize = 16.sp,
        lineHeight = 22.sp
    ),
    bodyLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 15.sp,
        lineHeight = 21.sp
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 14.sp,
        lineHeight = 20.sp
    ),
    bodySmall = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Normal,
        fontSize = 12.sp,
        lineHeight = 17.sp
    ),
    labelLarge = TextStyle(
        fontFamily = FontFamily.Default,
        fontWeight = FontWeight.Medium,
        fontSize = 14.sp,
        lineHeight = 19.sp
    )
)

@Composable
fun AssetFlowTheme(
    oscuro: Boolean = isSystemInDarkTheme(),
    contenido: @Composable () -> Unit
) {
    val esquema = if (oscuro) EsquemaOscuro else EsquemaClaro
    val vista = LocalView.current

    if (!vista.isInEditMode) {
        SideEffect {
            val ventana = (vista.context as Activity).window
            WindowCompat.getInsetsController(ventana, vista)
                .isAppearanceLightStatusBars = !oscuro
        }
    }

    MaterialTheme(
        colorScheme = esquema,
        typography = Tipografia,
        content = contenido
    )
}
