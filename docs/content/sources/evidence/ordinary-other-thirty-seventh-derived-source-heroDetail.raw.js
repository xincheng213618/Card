// pc武将皮肤切换
const heroNav1 = new Swiper("#heroNav1", {
    direction: "vertical",
    slidesPerView: 5,
    observer: true,
    observeParents: true,
    observeSlideChildren: true,
    navigation: {
        prevEl: ".detail-nav-swiper-prev",
        nextEl: ".detail-nav-swiper-next",
    },
    // loop: true,
    // centeredSlides: true,
});

$('#heroNav1 .swiper-slide').click(function () {
    heroNav1.slideTo($(this).index())
})

// 技能切换
$('.character-tab').click(function () {
    $(this).addClass('on').siblings('div').removeClass('on')
    $('.skill-text span').eq($(this).index()).addClass('on').siblings('span').removeClass('on')
})

// 详情页点击右侧头像显示皮肤
$('#heroNav1 img').click(function () {
    // $('.art-alert').attr('src', $(this).attr('data-url'))
    $('.hero-detail-card > img').attr('src', $(this).attr('data-url'))
    // $('.alert-box').fadeIn()
})

$('.art-close').click(function () {
    $('.alert-box').fadeOut()
    $('.art-alert').attr('src', ' ')
})

$('.hero-detail-card img').click(function () {
    $('.art-alert').attr('src', $(this).attr('src'))
    $('.alert-box').fadeIn()
})

// 初始化武将详情页面
function initHeroDetail() {
    let historyList = [];

    if (info && info.name) {
        $('.hero-name').text(info.name);
    }

    if (typeof cardData !== 'undefined' && !!cardData && Array.isArray(cardData)) {
        let hainan = false;
        cardData.forEach(E => {
            if (E.name == '海南省') {
                hainan = true;
                E.itemStyle.normal.borderColor = "#a7a7a7";
                E.itemStyle.emphasis.borderColor = "#a7a7a7";
            }
            E.list.forEach(e => {
                historyList.push({
                    name: E.name, ...e
                });
            });
        });
        if (!hainan) {
            cardData.push({
                name: '海南省', code: 130000, list: [], itemStyle: {
                    normal: {
                        borderColor: "#a7a7a7"
                    }, emphasis: {
                        borderColor: "#a7a7a7"
                    },
                }
            }, {
                name: '边线', code: 130000, list: [], itemStyle: {
                    normal: {
                        borderColor: "#a7a7a7"
                    }, emphasis: {
                        borderColor: "#a7a7a7"
                    },
                }
            })
        } else {
            cardData.push({
                name: '边线', code: 130000, list: [], itemStyle: {
                    normal: {
                        borderColor: "#a7a7a7"
                    }, emphasis: {
                        borderColor: "#a7a7a7"
                    },
                }
            })
        }
        let l = ''
        historyList.forEach(e => {
            l += `<li>${e.content}</li>`
        });
        $('.history-list').html(l)
    }

    if (info && info.hp !== undefined) {
        var detailHp = info.hp
        var initial_hp = info.initial_hp || 0
        let star = ``
        let guozhan = info.name && info.name.indexOf('国战') > -1

        if (initial_hp) {
            for (let index = 0; index < (detailHp - initial_hp); index++) {
                star += `<li><img src="/assets/img/pc/xue-empty.png" alt=""></li>`
            }

            for (let index = 0; index < initial_hp; index++) {
                star += `<li><img src="/assets/img/pc/${guozhan ? 'xue-guo' : 'xue'}.png" alt=""></li>`
            }
        } else {
            if (Number.isInteger(detailHp)) {
                for (let index = 0; index < detailHp; index++) {
                    star += `<li><img src="/assets/img/pc/${guozhan ? 'xue-guo' : 'xue'}.png" alt=""></li>`
                }
            } else {
                for (let index = 0; index < Math.floor(detailHp); index++) {
                    star += `<li><img src="/assets/img/pc/xue-guo.png" alt=""></li>`
                }
                star += `<li><img src="/assets/img/pc/xue-half.png" alt=""></li>`
            }
        }
        $('.hero-life').html(star)
    }

    // 雷达图和寻迹图初始化
    initCharts(historyList);
}

// 图表初始化
function initCharts(historyList) {
    let isMobile = (window.innerWidth < 750) ? true : false;
    var heroChartDom = document.getElementById('hero-echarts');

    if (!!heroChartDom && (!!radarData || radarData.length > 0)) {
        var heroEcharts = echarts.init(heroChartDom);
        var heroOption;
        var heroData = []
        var axiosData = []

        radarData.forEach(item => {
            heroData.push(item.value)
            axiosData.push({ name: item.name, max: 10, })
        });

        // 如果雷达数据多于1个元素，检查第二个元素的名称长度
        if (axiosData.length > 1 && axiosData[1] && axiosData[1].name && axiosData[1].name.length > 2) {
            let a = axiosData[1].name
            axiosData[1].name = axiosData[2].name
            axiosData[2].name = a
            let b = heroData[1]
            heroData[1] = heroData[2]
            heroData[2] = b
        }

        heroOption = {
            radar: {
                indicator: axiosData,
                splitArea: {
                    areaStyle: {
                        color: ['#fff', '#f7f7f7', '#ebebeb', '#f7f7f7']
                    }
                },
                radius: isMobile ? "60%" : 75,
                startAngle: 90,
                splitNumber: 4,
                shape: 'circle',
                axisName: {
                    color: '#000',
                    fontSize: '16px'
                },
                splitLine: {
                    lineStyle: {
                        color: 'rgba(211, 253, 250, 0)'
                    }
                },
                axisLine: {
                    lineStyle: {
                        color: 'rgba(179, 179, 179, 0.2)'
                    }
                }
            },
            series: [{
                name: 'Budget vs spending',
                type: 'radar',
                data: [{
                    value: heroData,
                    name: 'Allocated Budget',
                    itemStyle: {
                        normal: {
                            color: 'rgba(0,0,0,0)',
                            borderColor: '#FFAE34',
                            borderWidth: 0
                        }
                    },
                    lineStyle: {
                        color: 'rgba(0,0,0,0)'
                    }
                }],
                areaStyle: {
                    color: new echarts.graphic.RadialGradient(0.5, 0.5, 0.5, [{
                        color: 'rgba(174, 150, 103, 0.5)',
                        offset: 0
                    }, {
                        color: 'rgba(174, 150, 103, 0.5)',
                        offset: 1
                    }])
                },
            }, {
                name: 'points',
                type: 'radar',
                symbolSize: 5,
                data: [{
                    value: [10, 10, 10, 10, 10],
                    name: 'Allocated Budget',
                    itemStyle: {
                        normal: {
                            color: 'rgba(255, 255, 255, 1)',
                            borderColor: 'rgba(112, 112, 112, 0.3)',
                            borderWidth: 1
                        }
                    },
                    lineStyle: {
                        color: 'rgba(0,0,0,0)'
                    }
                }],
                areaStyle: {
                    color: new echarts.graphic.RadialGradient(0.5, 0.5, 0.5, [{
                        color: 'rgba(174, 150, 103, 0)',
                        offset: 0
                    }, {
                        color: 'rgba(174, 150, 103, 0)',
                        offset: 1
                    }])
                },
            }]
        };

        heroOption && heroEcharts.setOption(heroOption);
    }

    // 武将寻迹-pc
    var hisChartDom = document.getElementById('history-echarts');
    if (!!hisChartDom) {
        var hisChart = echarts.init(hisChartDom);
        var hisOption;

        $.get('/assets/js/china.json', function (usaJson) {
            echarts.registerMap('China', usaJson, {});
            hisOption = {
                tooltip: {
                    show: false
                },
                series: [{
                    name: 'China',
                    type: 'map',
                    roam: false,
                    map: 'China',
                    selectedMode: 'none',
                    itemStyle: {
                        normal: {
                            areaColor: '#eceae7',
                            borderColor: '#fff'
                        },
                        emphasis: {
                            areaColor: '#a88462',
                            borderColor: '#fff'
                        },
                    },
                    data: cardData.map(item => {
                        if (item && item.index) {
                            return {
                                ...item,
                                itemStyle: {
                                    areaColor: '#a88462'
                                }
                            };
                        }
                        return item;
                    }),
                    label: {
                        show: true,
                        renderAsImage: false,
                        formatter: function (params) {
                            if (!!params.data && !!params.data.index) {
                                return '{a|' + params.data.index + '}';
                            } else {
                                return '';
                            }
                        },
                        rich: {
                            a: {
                                color: '#a88462',
                                backgroundColor: 'white',
                                fontSize: 15,
                                lineHeight: 16,
                                borderWidth: 1,
                                borderColor: '#a88462',
                                borderRadius: 15,
                                width: 16,
                                height: 16,
                                textAlign: 'center'
                            },
                        }
                    },
                }]
            };
            hisChart.setOption(hisOption);
        });

        function onMapClick(res) {
            if (!!res.data && !!res.data.index) {
                $('.info-card').html(`
                    <div class="close"></div>
                    <img src="${res.data.list[0].image}" class="info-card-img" alt="">
                    <h5>${res.data.list[0].title}</h5>
                    <p class="address">[地址]:<span>${res.data.list[0].address}</span></p>
                    <p class="antique">[文保级别]:<span>${res.data.list[0].antique}</span></p>
                    <p class="attraction">[景区级别]:<span>${res.data.list[0].attraction}</span></p>
                    <p class="info-text">${res.data.list[0].brief}</p>
                `).fadeIn()
                let list = ''

                res.data.list.forEach(item => {
                    list += `<li>${item.content}</li>`
                });
                $('.history-list').html(list)

                $('.history-list li').on("mouseenter", function () {
                    setCard($(this).index())
                })
                $('.history-list li').on("mouseleave", function () {
                    $('.info-card').css({ 'display': 'none' })
                })

                $('.close').click(function () {
                    $('.info-card').fadeOut()
                })
            }
        }

        hisChart.on('click', onMapClick);

        $('.history-list li').click(function () {
            $('.info-card').fadeOut()
            setTimeout(() => {
                $('.info-card').html(`
                    <div class="close"></div>
                    <img src="${historyList[$(this).index()].image}" class="info-card-img" alt="">
                    <h5>${historyList[$(this).index()].title}</h5>
                    <p class="address">[地址]:<span>${historyList[$(this).index()].address}</span></p>
                    <p class="antique">[文保级别]:<span>${historyList[$(this).index()].antique}</span></p>
                    <p class="attraction">[景区级别]:<span>${historyList[$(this).index()].attraction}</span></p>
                    <p class="info-text">${historyList[$(this).index()].brief}</p>
                `)
                $('.info-card').fadeIn()
                $('.close').click(function () {
                    $('.info-card').fadeOut()
                })
            }, 500);
        })

        function setCard(index) {
            $('.info-card').css({ display: 'none' });
            setTimeout(() => {
                $('.info-card').html(`
                    <div class="close"></div>
                    <img src="${historyList[index].image}" class="info-card-img" alt="">
                    <h5>${historyList[index].title}</h5>
                    <p class="address">[地址]:<span>${historyList[index].address}</span></p>
                    <p class="antique">[文保级别]:<span>${historyList[index].antique}</span></p>
                    <p class="attraction">[景区级别]:<span>${historyList[index].attraction}</span></p>
                    <p class="info-text">${historyList[index].brief}</p>
                `).fadeIn();
                $('.close').click(function () {
                    $('.info-card').css({ display: 'none' });
                });
            }, 500);
        }

        $('.history-list li').on("mouseenter", function () {
            setCard($(this).index())
        })
        $('.history-list li').on("mouseleave", function () {
            $('.info-card').css({ 'display': 'none' })
        })
    }
}

// 监听数据加载事件
window.addEventListener('heroDataLoaded', function(e) {
    // 更新全局变量
    if (e.detail.info) info = e.detail.info;
    if (e.detail.radarData) radarData = e.detail.radarData;
    if (e.detail.cardData) cardData = e.detail.cardData;

    // 初始化页面
    initHeroDetail();
});

// 页面加载完成后检查数据是否已加载
window.addEventListener('load', function() {
    // 如果数据已经加载完成（从缓存或其他方式），则直接初始化
    if (info && radarData && cardData) {
        initHeroDetail();
    }
});