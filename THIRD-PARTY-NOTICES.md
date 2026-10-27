# Third-party notices

Folder Managing is licensed under the GNU General Public License, version 2 only (see `LICENSE`).
It ships with the following third-party components. Each one keeps its own license, reproduced below.
All of them use the MIT License, which is compatible with GPL-2.0.

| Component | Version | Used for | Project |
| --- | --- | --- | --- |
| WPF UI (`Wpf.Ui`, `Wpf.Ui.Abstractions`) | 4.2.1 | Fluent controls in the app and the installer | https://github.com/lepoco/wpfui |
| Vanara (`Vanara.Core`, `Vanara.PInvoke.*`) | 4.2.1 | Windows Shell interop in the Explorer extension | https://github.com/dahall/vanara |
| `System.Drawing.Common`, `Microsoft.Win32.SystemEvents` | 8.0.10 / 8.0.0 | Building folder icons | https://github.com/dotnet/winforms |
| `System.IO.Pipes.AccessControl`, `System.Security.AccessControl`, `System.Security.Principal.Windows` | 5.0.0 | Securing the elevated helper pipe | https://github.com/dotnet/runtime |
| .NET 8 Desktop Runtime (bundled with the installer) | 8.x | Running the app | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf, https://github.com/dotnet/winforms |

---

## WPF UI

MIT License

Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors. https://lepo.co/

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## Vanara

MIT License

Copyright (c) 2017-2025 David Hall

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## .NET libraries and the .NET 8 Desktop Runtime

Covers `System.Drawing.Common`, `Microsoft.Win32.SystemEvents`, `System.IO.Pipes.AccessControl`,
`System.Security.AccessControl`, `System.Security.Principal.Windows` and the bundled .NET 8 Desktop Runtime installer.

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
