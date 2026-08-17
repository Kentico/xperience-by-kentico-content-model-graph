const webpackMerge = require("webpack-merge");
const path = require("path");
const fs = require("fs");
const spawn = require("child_process").spawn;

const baseWebpackConfig = require("@kentico/xperience-webpack-config");
const { cert, key } = getDotnetCertPaths();

module.exports = (opts, argv) => {
  const baseConfig = (webpackConfigEnv, webpackArgv) =>
    baseWebpackConfig({
      orgName: "kentico",
      projectName: "xperience-content-model-graph",
      webpackConfigEnv,
      argv: webpackArgv,
    });

  return new Promise((resolve) => {
    if (
      argv.mode === "production" ||
      (fs.existsSync(cert) && fs.existsSync(key))
    ) {
      resolve(buildConfig(baseConfig, opts, argv));
      return;
    }

    spawn(
      "dotnet",
      [
        "dev-certs",
        "https",
        "--export-path",
        cert,
        "--format",
        "Pem",
        "--no-password",
      ],
      { stdio: "inherit" },
    ).on("exit", (code) => {
      resolve(buildConfig(baseConfig, opts, argv));
      if (code) {
        process.exit(code);
      }
    });
  });
};

function buildConfig(baseConfig, opts, argv) {
  const projectConfig = {
    module: {
      rules: [
        {
          test: /\.(js|ts)x?$/,
          exclude: [/node_modules/],
          loader: "babel-loader",
        },
        {
          test: /\.css$/,
          use: ["style-loader", "css-loader"],
        },
      ],
    },
    output: {
      clean: true,
      chunkFormat: false,
    },
    devServer: {
      port: 3019,
      server: {
        type: "https",
        options: {
          key,
          cert,
        },
      },
    },
  };

  return webpackMerge.merge(projectConfig, baseConfig(opts, argv));
}

function getDotnetCertPaths() {
  const baseFolder =
    process.env.APPDATA !== undefined && process.env.APPDATA !== ""
      ? `${process.env.APPDATA}/ASP.NET/https`
      : `${process.env.HOME}/.aspnet/https`;

  fs.mkdirSync(baseFolder, { recursive: true });

  const certificateName = process.env.npm_package_name;
  const cert = path.join(baseFolder, `${certificateName}.pem`);
  const key = path.join(baseFolder, `${certificateName}.key`);

  return { cert, key };
}
