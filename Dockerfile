FROM mcr.microsoft.com/dotnet/sdk:5.0 as builder

RUN apt-get update && apt-get install curl build-essential -y
RUN curl -sL https://deb.nodesource.com/setup_15.x | bash -
RUN apt-get update && apt-get install -y nodejs
RUN npm install -g yarn

RUN mkdir /diffix-prototype
WORKDIR /diffix-prototype
COPY . /diffix-prototype/

WORKDIR /diffix-prototype/WebFrontend
RUN yarn install
RUN make build-css

WORKDIR /diffix-prototype
RUN mkdir build
RUN dotnet publish WebFrontend -c Production -r linux-x64 --output build


FROM mcr.microsoft.com/dotnet/runtime:5.0

COPY --from=builder /diffix-prototype/build/ /release
COPY --from=builder /diffix-prototype/WebFrontend/wwwroot/ /wwwroot
CMD /release/WebFrontend
