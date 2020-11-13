FROM mcr.microsoft.com/dotnet/sdk:5.0 as builder

RUN apt-get update && apt-get install curl build-essential -y
RUN curl -sL https://deb.nodesource.com/setup_15.x | bash -
RUN apt-get update && apt-get install -y nodejs
RUN npm install -g yarn

# Do the CSS generation separately. Unlikely to change much,
# and takes an awful lot of time!
RUN mkdir /assets
WORKDIR /assets
COPY WebFrontend/package.json /assets/
COPY WebFrontend/yarn.lock /assets/
RUN yarn install

# And now the rest of the web app
COPY WebFrontend/ /assets
RUN make build-css

RUN mkdir /diffix-prototype
WORKDIR /diffix-prototype
COPY . /diffix-prototype/
RUN mkdir build
RUN dotnet publish WebFrontend -c Production -r linux-x64 --output build


FROM mcr.microsoft.com/dotnet/runtime:5.0

COPY --from=builder /diffix-prototype/build/ /release
COPY --from=builder /assets/wwwroot/ /wwwroot
CMD /release/WebFrontend
