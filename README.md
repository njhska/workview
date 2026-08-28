# Code Shelf

基于 .NET 10 Minimal API 的只读服务器文件浏览与代码预览网站，支持 Native AOT 发布。

## 本地运行

```bash
FILE_BROWSER_ROOT=/需要浏览的目录 dotnet run
```

访问 `http://localhost:5080`。默认仅预览常见 .NET/前端文本文件，单文件上限 2 MB；目录项按最后修改时间降序排列。符号链接不会显示，所有请求路径都会限制在配置的根目录内。

## 发布到 Ubuntu

在开发机发布 Ubuntu x64 单文件原生程序：

```bash
dotnet publish -c Release -r linux-x64 --self-contained -o ./publish
```

将 `publish` 目录复制到服务器的 `/opt/file-browser`，为可执行文件添加权限，并按需修改 `deploy/file-browser.service` 中的 `FILE_BROWSER_ROOT`。然后：

```bash
sudo cp deploy/file-browser.service /etc/systemd/system/
sudo chmod +x /opt/file-browser/FileBrowser
sudo chown -R www-data:www-data /opt/file-browser
sudo systemctl daemon-reload
sudo systemctl enable --now file-browser
```

建议用 Nginx/Caddy 在 `127.0.0.1:5080` 前提供 HTTPS 和访问认证。若目标目录不是 `/srv/code`，同步修改 service 文件中的 `ReadOnlyPaths`。
