using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp7
{
    /// <summary>
    /// Provides a lightweight wrapper around the Windows MessageBox API for displaying simple dialog boxes.
    /// This implementation uses P/Invoke to call the native Windows API directly, making it suitable for
    /// console applications where Windows Forms is not referenced.
    /// </summary>
    public class MessageBox
    {
        /// <summary>
        /// P/Invoke declaration for the Windows MessageBoxW function.
        /// Uses Unicode (wide-char) version for proper character encoding support.
        /// 
        /// Parameters:
        /// - hWnd: Handle to the owner window
        /// - lpText: The message to display
        /// - lpCaption: The dialog box title
        /// - uType: The buttons and icons to display
        /// 
        /// Security note: This P/Invoke call is safe as it only displays UI and doesn't modify system state.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);


        /// <summary>
        /// Displays a message box with the specified text and title.
        /// This method provides a simplified interface to the Windows MessageBox API,
        /// showing only an OK button and using the default icon.
        /// </summary>
        /// <param name="message">The message to display in the dialog box</param>
        /// <param name="title">The title of the dialog box</param>
        /// <remarks>
        /// - Uses IntPtr.Zero as the owner window, making the message box application-modal
        /// - Sets uType to 0 for the simplest OK-button-only configuration
        /// - Unicode support is enabled through CharSet.Unicode in the DllImport attribute
        /// </remarks>
        public static int Show(string message, string title, uint type = 0)
        {
            return MessageBoxW(IntPtr.Zero, message, title, type);
        }

    }
}